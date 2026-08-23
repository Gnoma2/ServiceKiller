using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class ProcessGroupMetrics
    {
        internal ProcessGroupMetrics()
        {
            ProcessIds = new HashSet<int>();
            RootPids = new List<int>();
        }

        public int ProcessCount { get { return ProcessIds.Count; } }
        public int RootProcessCount { get { return RootPids.Count; } }
        public long WorkingSetBytes { get; set; }
        public long WorkingSetMb { get { return WorkingSetBytes <= 0 ? 0 : (WorkingSetBytes + 1024L * 1024L - 1L) / (1024L * 1024L); } }
        internal HashSet<int> ProcessIds { get; private set; }
        internal List<int> RootPids { get; private set; }
    }

    public sealed class ProcessCloseResult
    {
        public int InitialProcessCount { get; set; }
        public int RemainingProcessCount { get; set; }
        public int ClosedProcessCount { get; set; }
        public int TreesRequested { get; set; }
        public int ServicesStopped { get; set; }
        public long MemoryBeforeMb { get; set; }
        public long MemoryAfterMb { get; set; }
        public bool Remains { get; set; }
    }

    internal sealed class ProcessRecord
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public long WorkingSetBytes { get; set; }
        public bool HasMainWindow { get; set; }
    }

    public sealed class ProcessManager
    {
        private readonly Logger _log;
        private readonly WindowsServiceManager _services;

        public ProcessManager(Logger log, WindowsServiceManager services)
        {
            _log = log;
            _services = services;
        }

        // Compatibilidad con el motor anterior. V1.03 usa CloseDetailedForTweak
        // para poder mostrar cuántos procesos se cerraron realmente.
        public int CloseForTweak(TweakDefinition tweak)
        {
            return CloseDetailedForTweak(tweak).ClosedProcessCount;
        }

        public ProcessCloseResult CloseDetailedForTweak(TweakDefinition tweak)
        {
            ProcessCloseResult result = new ProcessCloseResult();
            ProcessGroupMetrics before = MeasureForTweak(tweak);
            result.InitialProcessCount = before.ProcessCount;
            result.MemoryBeforeMb = before.WorkingSetMb;

            // Primero se detienen servicios residentes conocidos. Esto reduce el riesgo
            // de que un agente vuelva a crear el proceso mientras cerramos su árbol.
            foreach (string serviceText in tweak.TemporaryServiceNameContains)
            {
                List<string> stopped = _services.StopServicesContaining(serviceText);
                result.ServicesStopped += stopped.Count;
            }

            // Dos pasadas como máximo. La segunda solo sirve para procesos que hayan
            // aparecido mientras se estaba cerrando el primer árbol.
            HashSet<int> requestedRoots = new HashSet<int>();
            for (int pass = 0; pass < 2; pass++)
            {
                ProcessGroupMetrics group = MeasureForTweak(tweak);
                if (group.RootPids.Count == 0) break;

                bool requestedSomething = false;
                foreach (int rootPid in group.RootPids)
                {
                    if (!requestedRoots.Add(rootPid)) continue;
                    requestedSomething = true;
                    result.TreesRequested++;

                    int killed = KillProcessTreeManaged(rootPid);
                    if (!ProcessExists(rootPid))
                    {
                        _log.Info("Árbol de procesos cerrado desde PID " + rootPid + " (" + tweak.Name + ", " + killed + " proceso(s) solicitados)");
                    }
                    else
                    {
                        _log.Warn("No se pudo cerrar completamente el árbol PID " + rootPid + " para " + tweak.Name + ".");
                    }
                }

                if (!requestedSomething) break;
                Thread.Sleep(180);
            }

            ProcessGroupMetrics after = MeasureForTweak(tweak);
            result.RemainingProcessCount = after.ProcessCount;
            result.MemoryAfterMb = after.WorkingSetMb;

            int closed = 0;
            foreach (int pid in before.ProcessIds)
            {
                if (!after.ProcessIds.Contains(pid) && !ProcessExists(pid)) closed++;
            }
            result.ClosedProcessCount = closed;
            result.Remains = after.ProcessCount > 0;
            foreach (string serviceText in tweak.TemporaryServiceNameContains)
                if (_services.IsAnyServiceContainingRunning(serviceText)) result.Remains = true;

            if (before.ProcessCount > 0 || result.ServicesStopped > 0)
            {
                _log.Info(tweak.Name + ": procesos " + before.ProcessCount + " -> " + after.ProcessCount +
                          ", RAM asociada " + before.WorkingSetMb + " MB -> " + after.WorkingSetMb + " MB" +
                          (result.ServicesStopped > 0 ? ", servicios detenidos: " + result.ServicesStopped : ""));
            }
            return result;
        }

        public ProcessGroupMetrics MeasureForTweak(TweakDefinition tweak)
        {
            Dictionary<int, int> parentMap = SnapshotParentMap();
            List<ProcessRecord> records = new List<ProcessRecord>();
            HashSet<int> directIds = new HashSet<int>();

            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { return new ProcessGroupMetrics(); }

            int currentSession = -1;
            try { using (Process current = Process.GetCurrentProcess()) currentSession = current.SessionId; } catch { }

            foreach (Process process in processes)
            {
                try
                {
                    // No se cierran procesos de otra sesión de usuario. El worker UAC
                    // permanece en la misma sesión interactiva que la GUI.
                    if (currentSession >= 0)
                    {
                        try { if (process.SessionId != currentSession) continue; }
                        catch { continue; }
                    }

                    ProcessRecord record = new ProcessRecord();
                    record.Id = process.Id;
                    int parent;
                    record.ParentId = parentMap.TryGetValue(record.Id, out parent) ? parent : 0;
                    try { record.Name = process.ProcessName ?? string.Empty; } catch { record.Name = string.Empty; }
                    try { record.Path = process.MainModule == null ? string.Empty : (process.MainModule.FileName ?? string.Empty); } catch { record.Path = string.Empty; }
                    try { record.WorkingSetBytes = Math.Max(0L, process.WorkingSet64); } catch { record.WorkingSetBytes = 0L; }
                    try { record.HasMainWindow = process.MainWindowHandle != IntPtr.Zero; } catch { record.HasMainWindow = false; }
                    records.Add(record);
                    if (Matches(record, tweak)) directIds.Add(record.Id);
                }
                catch { }
                finally { process.Dispose(); }
            }

            ProcessGroupMetrics metrics = new ProcessGroupMetrics();
            if (directIds.Count == 0) return metrics;

            // Solo cerramos/contamos raíces de la familia. Si varios procesos coinciden
            // pero uno ya es hijo de otro coincidente, el cierre administrado del árbol padre basta.
            HashSet<int> roots = new HashSet<int>();
            foreach (int pid in directIds)
            {
                if (!HasAncestorInSet(pid, directIds, parentMap)) roots.Add(pid);
            }
            foreach (int root in roots) metrics.RootPids.Add(root);

            // El grupo completo incluye descendientes aunque su nombre sea distinto
            // (WebView, crash handlers, helpers, etc.). Esto hace más fiel la RAM mostrada.
            foreach (ProcessRecord record in records)
            {
                if (IsDescendantOfAny(record.Id, roots, parentMap))
                {
                    metrics.ProcessIds.Add(record.Id);
                    metrics.WorkingSetBytes += record.WorkingSetBytes;
                }
            }
            return metrics;
        }

        public List<ResidentProcessCandidate> DiscoverResidentCandidates()
        {
            Dictionary<int, int> parentMap = SnapshotParentMap();
            Dictionary<int, ProcessRecord> records = new Dictionary<int, ProcessRecord>();
            int currentSession = -1;
            try { using (Process current = Process.GetCurrentProcess()) currentSession = current.SessionId; } catch { }

            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { return new List<ResidentProcessCandidate>(); }

            foreach (Process process in processes)
            {
                try
                {
                    if (currentSession >= 0)
                    {
                        try { if (process.SessionId != currentSession) continue; }
                        catch { continue; }
                    }

                    ProcessRecord record = new ProcessRecord();
                    record.Id = process.Id;
                    int parent;
                    record.ParentId = parentMap.TryGetValue(record.Id, out parent) ? parent : 0;
                    try { record.Name = process.ProcessName ?? string.Empty; } catch { record.Name = string.Empty; }
                    try { record.Path = process.MainModule == null ? string.Empty : (process.MainModule.FileName ?? string.Empty); } catch { record.Path = string.Empty; }
                    try { record.WorkingSetBytes = Math.Max(0L, process.WorkingSet64); } catch { record.WorkingSetBytes = 0L; }
                    try { record.HasMainWindow = process.MainWindowHandle != IntPtr.Zero; } catch { record.HasMainWindow = false; }
                    records[record.Id] = record;
                }
                catch { }
                finally { process.Dispose(); }
            }

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int selfPid = -1;
            try { selfPid = Process.GetCurrentProcess().Id; } catch { }
            Dictionary<int, List<ProcessRecord>> groups = new Dictionary<int, List<ProcessRecord>>();

            foreach (ProcessRecord record in records.Values)
            {
                if (record.Id == selfPid || IsScannerIgnored(record.Name, record.Path, windows)) continue;
                int root = FindApplicationRoot(record.Id, records, windows);
                ProcessRecord rootRecord;
                if (!records.TryGetValue(root, out rootRecord)) rootRecord = record;
                if (IsScannerIgnored(rootRecord.Name, rootRecord.Path, windows)) continue;

                List<ProcessRecord> list;
                if (!groups.TryGetValue(rootRecord.Id, out list))
                {
                    list = new List<ProcessRecord>();
                    groups[rootRecord.Id] = list;
                }
                list.Add(record);
            }

            List<ResidentProcessCandidate> candidates = new List<ResidentProcessCandidate>();
            foreach (KeyValuePair<int, List<ProcessRecord>> pair in groups)
            {
                ProcessRecord root;
                if (!records.TryGetValue(pair.Key, out root)) continue;
                long bytes = 0L;
                bool mainWindow = false;
                foreach (ProcessRecord item in pair.Value)
                {
                    bytes += Math.Max(0L, item.WorkingSetBytes);
                    if (item.HasMainWindow) mainWindow = true;
                }

                // Para evitar ruido, solo sugerimos árboles con UI o con una residencia
                // apreciable. El usuario puede seguir añadiendo manualmente cualquier EXE.
                long mb = bytes <= 0 ? 0 : (bytes + 1024L * 1024L - 1L) / (1024L * 1024L);
                if (!mainWindow && mb < 40) continue;
                if (string.IsNullOrWhiteSpace(root.Path)) continue;

                ResidentProcessCandidate candidate = new ResidentProcessCandidate();
                candidate.RootPid = root.Id;
                candidate.ProcessName = root.Name;
                candidate.ExecutablePath = root.Path;
                candidate.ProcessCount = pair.Value.Count;
                candidate.MemoryMb = mb;
                candidate.HasMainWindow = mainWindow;
                candidate.DisplayName = FriendlyProcessName(root.Path, root.Name);
                candidate.Note = mainWindow ? "Aplicación interactiva detectada" : "Proceso residente con consumo apreciable";
                candidates.Add(candidate);
            }

            return candidates
                .OrderByDescending(delegate(ResidentProcessCandidate c) { return c.MemoryMb; })
                .ThenBy(delegate(ResidentProcessCandidate c) { return c.DisplayName; }, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static int FindApplicationRoot(int pid, Dictionary<int, ProcessRecord> records, string windowsPath)
        {
            int current = pid;
            HashSet<int> seen = new HashSet<int>();
            while (seen.Add(current))
            {
                ProcessRecord record;
                if (!records.TryGetValue(current, out record)) break;
                int parentId = record.ParentId;
                ProcessRecord parent;
                if (parentId <= 0 || !records.TryGetValue(parentId, out parent)) break;
                if (IsBoundaryProcess(parent.Name, parent.Path, windowsPath)) break;
                current = parentId;
            }
            return current;
        }

        private static bool IsBoundaryProcess(string name, string path, string windowsPath)
        {
            if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)) return true;
            return IsWindowsPath(path, windowsPath);
        }

        private static bool IsScannerIgnored(string name, string path, string windowsPath)
        {
            string[] ignored = new string[]
            {
                "System", "Idle", "Registry", "Memory Compression", "explorer", "dwm", "ctfmon",
                "sihost", "taskhostw", "SearchHost", "SearchApp", "StartMenuExperienceHost",
                "ShellExperienceHost", "RuntimeBroker", "ApplicationFrameHost", "conhost",
                "OpenConsole", "WindowsTerminal", "cmd", "powershell", "pwsh", "taskmgr",
                "ServiceKillerV1", "ServiceKiller"
            };
            foreach (string item in ignored)
                if (string.Equals(name, item, StringComparison.OrdinalIgnoreCase)) return true;
            return IsWindowsPath(path, windowsPath);
        }

        private static bool IsWindowsPath(string path, string windowsPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(windowsPath)) return false;
            try
            {
                string full = System.IO.Path.GetFullPath(path).TrimEnd('\\') + "\\";
                string win = System.IO.Path.GetFullPath(windowsPath).TrimEnd('\\') + "\\";
                return full.StartsWith(win, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string FriendlyProcessName(string path, string fallback)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string text = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch { }
            return string.IsNullOrWhiteSpace(fallback) ? "Aplicación" : fallback;
        }

        public bool IsAnyRunning(TweakDefinition tweak)
        {
            if (MeasureForTweak(tweak).ProcessCount > 0) return true;
            foreach (string serviceText in tweak.TemporaryServiceNameContains)
                if (_services.IsAnyServiceContainingRunning(serviceText)) return true;
            return false;
        }

        private static bool HasAncestorInSet(int pid, HashSet<int> candidateAncestors, Dictionary<int, int> parentMap)
        {
            HashSet<int> seen = new HashSet<int>();
            int current = pid;
            int parent;
            while (parentMap.TryGetValue(current, out parent) && parent > 0 && seen.Add(parent))
            {
                if (candidateAncestors.Contains(parent)) return true;
                current = parent;
            }
            return false;
        }

        private static bool IsDescendantOfAny(int pid, HashSet<int> roots, Dictionary<int, int> parentMap)
        {
            if (roots.Contains(pid)) return true;
            HashSet<int> seen = new HashSet<int>();
            int current = pid;
            int parent;
            while (parentMap.TryGetValue(current, out parent) && parent > 0 && seen.Add(parent))
            {
                if (roots.Contains(parent)) return true;
                current = parent;
            }
            return false;
        }

        private static int KillProcessTreeManaged(int rootPid)
        {
            Dictionary<int, int> parentMap = SnapshotParentMap();
            List<int> tree = new List<int>();
            foreach (int pid in parentMap.Keys)
            {
                if (pid == rootPid || IsDescendantOf(pid, rootPid, parentMap)) tree.Add(pid);
            }
            if (!tree.Contains(rootPid)) tree.Add(rootPid);

            // Hijos antes que padres para evitar que queden helpers huérfanos.
            tree.Sort(delegate(int a, int b)
            {
                return ProcessDepth(b, rootPid, parentMap).CompareTo(ProcessDepth(a, rootPid, parentMap));
            });

            int rootSession = -1;
            try { using (Process root = Process.GetProcessById(rootPid)) rootSession = root.SessionId; } catch { }

            int requested = 0;
            foreach (int pid in tree)
            {
                try
                {
                    using (Process process = Process.GetProcessById(pid))
                    {
                        if (rootSession >= 0)
                        {
                            try { if (process.SessionId != rootSession) continue; } catch { continue; }
                        }
                        requested++;
                        process.Kill();
                        try { process.WaitForExit(1500); } catch { }
                    }
                }
                catch { }
            }
            return requested;
        }

        private static bool IsDescendantOf(int pid, int rootPid, Dictionary<int, int> parentMap)
        {
            HashSet<int> seen = new HashSet<int>();
            int current = pid;
            int parent;
            while (parentMap.TryGetValue(current, out parent) && parent > 0 && seen.Add(parent))
            {
                if (parent == rootPid) return true;
                current = parent;
            }
            return false;
        }

        private static int ProcessDepth(int pid, int rootPid, Dictionary<int, int> parentMap)
        {
            int depth = 0;
            int current = pid;
            int parent;
            HashSet<int> seen = new HashSet<int>();
            while (current != rootPid && parentMap.TryGetValue(current, out parent) && parent > 0 && seen.Add(parent))
            {
                depth++;
                current = parent;
            }
            return depth;
        }

        private static bool ProcessExists(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid)) return true;
            }
            catch { return false; }
        }

        private static bool Matches(ProcessRecord process, TweakDefinition tweak)
        {
            string processName = process.Name ?? string.Empty;

            if (tweak.ProcessPaths.Count > 0 && !string.IsNullOrWhiteSpace(process.Path))
            {
                foreach (string expected in tweak.ProcessPaths)
                    if (PathsEqual(process.Path, expected)) return true;
                // Igual que en V1.02.x: si pudimos leer la ruta y no coincide,
                // no cerramos por una simple coincidencia de nombre.
                return false;
            }

            foreach (string item in tweak.ProcessNames)
                if (string.Equals(processName, item, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string prefix in tweak.ProcessPrefixes)
                if (processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                left = System.IO.Path.GetFullPath(left.Trim().Trim('"')).TrimEnd('\\');
                right = System.IO.Path.GetFullPath(right.Trim().Trim('"')).TrimEnd('\\');
            }
            catch { }
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<int, int> SnapshotParentMap()
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE) return map;
            try
            {
                PROCESSENTRY32 entry = new PROCESSENTRY32();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32First(snapshot, ref entry)) return map;
                do
                {
                    map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                    entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                }
                while (Process32Next(snapshot, ref entry));
            }
            catch { }
            finally { CloseHandle(snapshot); }
            return map;
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
