using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class StartupManager
    {
        private static readonly string[] RunKeys = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        private readonly Logger _log;
        private readonly string _userSidOverride;
        private List<ScheduledTaskSnapshot> _scheduledTaskCache;
        private DateTime _scheduledTaskCacheUtc;

        public sealed class ScheduledTaskMatchInfo
        {
            public string TaskPath { get; set; }
            public string TaskName { get; set; }
            public string Action { get; set; }
            public bool Enabled { get; set; }

            public string FullName
            {
                get
                {
                    string path = TaskPath ?? "\\";
                    if (!path.StartsWith("\\", StringComparison.Ordinal)) path = "\\" + path;
                    if (!path.EndsWith("\\", StringComparison.Ordinal)) path += "\\";
                    return path + (TaskName ?? string.Empty);
                }
            }
        }

        public StartupManager(Logger log)
            : this(log, null)
        {
        }

        public StartupManager(Logger log, string userSidOverride)
        {
            _log = log;
            _userSidOverride = userSidOverride;
        }

        public List<StartupEntryBackup> FindMatches(StartupRule rule)
        {
            List<StartupEntryBackup> found = new List<StartupEntryBackup>();
            ScanHive(RegistryHive.CurrentUser, "HKCU", rule, found);
            ScanHive(RegistryHive.LocalMachine, "HKLM", rule, found);
            ScanStartupFolders(rule, found);
            ScanScheduledLogonTasks(rule, found);
            return found;
        }

        public void RemoveEntries(IEnumerable<StartupEntryBackup> entries)
        {
            foreach (StartupEntryBackup entry in entries)
            {
                if (string.Equals(entry.EntryType, "File", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveStartupFile(entry);
                    continue;
                }
                if (string.Equals(entry.EntryType, "ScheduledTask", StringComparison.OrdinalIgnoreCase))
                {
                    DisableScheduledTask(entry);
                    continue;
                }

                RegistryHive hive = string.Equals(entry.Hive, "HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                RegistryView view = string.Equals(entry.RegistryView, "Registry32", StringComparison.OrdinalIgnoreCase) ? RegistryView.Registry32 : RegistryView.Registry64;
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = baseKey.OpenSubKey(entry.KeyPath, true))
                    {
                        if (key == null) continue;
                        object current = key.GetValue(entry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (current == null) continue;
                        key.DeleteValue(entry.ValueName, false);
                        _log.Info("Inicio automático eliminado: " + entry.ValueName + " -> " + Convert.ToString(current));
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("No se pudo eliminar inicio automático " + entry.ValueName + ": " + ex.Message, ex);
                }
            }
        }

        public bool HasMatch(StartupRule rule)
        {
            return FindMatches(rule).Count > 0;
        }

        // V1.1.2.8: diagnóstico de tareas de logon incluyendo las deshabilitadas.
        // FindMatches() sigue devolviendo solo mecanismos de arranque ACTIVOS para no
        // alterar la semántica de aplicación/restauración.
        public List<ScheduledTaskMatchInfo> FindScheduledTaskMatches(StartupRule rule)
        {
            List<ScheduledTaskMatchInfo> result = new List<ScheduledTaskMatchInfo>();
            if (rule == null || string.IsNullOrWhiteSpace(rule.MatchText)) return result;
            foreach (ScheduledTaskSnapshot item in GetScheduledTaskSnapshots())
            {
                string haystack = (item.TaskPath ?? string.Empty) + " " + (item.TaskName ?? string.Empty) + " " + (item.Action ?? string.Empty);
                if (haystack.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (LooksLikeUpdaterTask(haystack)) continue;
                result.Add(new ScheduledTaskMatchInfo
                {
                    TaskPath = item.TaskPath,
                    TaskName = item.TaskName,
                    Action = item.Action,
                    Enabled = item.Enabled
                });
            }
            return result;
        }

        public void Restore(StartupEntryBackup backup)
        {
            if (string.Equals(backup.EntryType, "File", StringComparison.OrdinalIgnoreCase))
            {
                RestoreStartupFile(backup);
                return;
            }
            if (string.Equals(backup.EntryType, "ScheduledTask", StringComparison.OrdinalIgnoreCase))
            {
                RestoreScheduledTask(backup);
                return;
            }

            RegistryView view = string.Equals(backup.RegistryView, "Registry32", StringComparison.OrdinalIgnoreCase) ? RegistryView.Registry32 : RegistryView.Registry64;
            RegistryKey baseKey = null;
            RegistryKey userRoot = null;
            try
            {
                if (string.Equals(backup.Hive, "HKCU", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_userSidOverride))
                {
                    baseKey = RegistryKey.OpenBaseKey(RegistryHive.Users, view);
                    userRoot = baseKey.OpenSubKey(_userSidOverride, true);
                    if (userRoot == null)
                        throw new InvalidOperationException("El hive del usuario " + _userSidOverride + " no está cargado; se reintentará la restauración en el próximo inicio de sesión.");
                }
                else
                {
                    RegistryHive hive = string.Equals(backup.Hive, "HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    userRoot = baseKey;
                }

                using (RegistryKey key = userRoot.CreateSubKey(backup.KeyPath))
                {
                    if (key == null) throw new InvalidOperationException("No se pudo restaurar inicio automático: " + backup.ValueName);
                    RegistryValueKind kind = RegistryValueKind.String;
                    if (!string.IsNullOrEmpty(backup.ValueKind))
                    {
                        try { kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), backup.ValueKind, true); }
                        catch { kind = RegistryValueKind.String; }
                    }
                    if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) kind = RegistryValueKind.String;
                    key.SetValue(backup.ValueName, backup.ValueData ?? string.Empty, kind);
                }
            }
            finally
            {
                if (userRoot != null && !object.ReferenceEquals(userRoot, baseKey)) userRoot.Dispose();
                if (baseKey != null) baseKey.Dispose();
            }
            RestoreStartupApprovals(backup);
            _log.Info("Inicio automático restaurado: " + backup.ValueName);
        }

        public bool MatchesBackup(StartupEntryBackup backup, out string detail)
        {
            detail = string.Empty;
            if (backup == null) { detail = "backup nulo"; return false; }
            try
            {
                if (string.Equals(backup.EntryType, "File", StringComparison.OrdinalIgnoreCase))
                {
                    bool exists = !string.IsNullOrWhiteSpace(backup.FilePath) && File.Exists(backup.FilePath);
                    detail = exists ? "archivo de Inicio presente" : "archivo de Inicio ausente";
                    return exists;
                }
                if (string.Equals(backup.EntryType, "ScheduledTask", StringComparison.OrdinalIgnoreCase))
                {
                    string fullName = ScheduledTaskFullName(backup);
                    ScheduledTaskSnapshot task = GetScheduledTaskSnapshots().Find(delegate(ScheduledTaskSnapshot x)
                    {
                        string xfull = ScheduledTaskFullName(x.TaskPath, x.TaskName);
                        return string.Equals(xfull, fullName, StringComparison.OrdinalIgnoreCase);
                    });
                    bool ok = task != null && (!backup.TaskWasEnabled || task.Enabled);
                    detail = task == null ? "tarea no encontrada" : (task.Enabled ? "tarea habilitada" : "tarea deshabilitada");
                    return ok;
                }

                RegistryView view = ParseView(backup.RegistryView);
                RegistryHive hive = string.Equals(backup.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                using (RegistryKey baseKey = OpenHiveForEntry(hive, view, false))
                using (RegistryKey key = baseKey == null ? null : baseKey.OpenSubKey(backup.KeyPath, false))
                {
                    if (key == null) { detail = "clave Run ausente"; return false; }
                    object current = key.GetValue(backup.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (current == null) { detail = "entrada Run ausente"; return false; }
                    string data = Convert.ToString(current);
                    string kind = key.GetValueKind(backup.ValueName).ToString();
                    if (!string.Equals(data ?? string.Empty, backup.ValueData ?? string.Empty, StringComparison.Ordinal) ||
                        !string.Equals(kind ?? string.Empty, backup.ValueKind ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        detail = "entrada Run distinta";
                        return false;
                    }
                }

                bool approvalOk = StartupApprovalsMatch(backup, out detail);
                return approvalOk;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }


        // V1.1.2.5: algunas aplicaciones (incluido software moderno que no usa Run)
        // arrancan mediante tareas programadas con LogonTrigger. Solo consideramos tareas
        // HABILITADAS que tengan un trigger de inicio de sesión y cuya ruta/nombre/acción
        // coincida con la regla. Nunca tocamos tareas de actualización sin LogonTrigger.
        private void ScanScheduledLogonTasks(StartupRule rule, List<StartupEntryBackup> results)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.MatchText)) return;
            List<ScheduledTaskSnapshot> snapshots = GetScheduledTaskSnapshots();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StartupEntryBackup existing in results)
                if (string.Equals(existing.EntryType, "ScheduledTask", StringComparison.OrdinalIgnoreCase))
                    seen.Add((existing.TaskPath ?? string.Empty) + "|" + (existing.TaskName ?? string.Empty));

            foreach (ScheduledTaskSnapshot item in snapshots)
            {
                if (!item.Enabled) continue;
                string haystack = (item.TaskPath ?? string.Empty) + " " + (item.TaskName ?? string.Empty) + " " + (item.Action ?? string.Empty);
                if (haystack.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (LooksLikeUpdaterTask(haystack)) continue;
                string identity = (item.TaskPath ?? string.Empty) + "|" + (item.TaskName ?? string.Empty);
                if (!seen.Add(identity)) continue;

                StartupEntryBackup backup = new StartupEntryBackup();
                backup.EntryType = "ScheduledTask";
                backup.TaskPath = item.TaskPath;
                backup.TaskName = item.TaskName;
                backup.TaskWasEnabled = item.Enabled;
                backup.ValueName = item.TaskName;
                backup.ValueData = item.Action;
                results.Add(backup);
            }
        }

        private List<ScheduledTaskSnapshot> GetScheduledTaskSnapshots()
        {
            if (_scheduledTaskCache != null && (DateTime.UtcNow - _scheduledTaskCacheUtc) < TimeSpan.FromSeconds(3))
                return _scheduledTaskCache;

            List<ScheduledTaskSnapshot> items = new List<ScheduledTaskSnapshot>();
            bool loaded = TryReadScheduledTasksViaCom(items);
            if (!loaded)
                _log.Warn("No se pudieron enumerar tareas de inicio mediante Task Scheduler 2.0 COM; se omite solo esa fuente de inicio.");

            _scheduledTaskCache = items;
            _scheduledTaskCacheUtc = DateTime.UtcNow;
            return items;
        }

        // Task Scheduler 2.0 COM está presente desde Windows Vista/7. V1.1.2.15
        // lo usa como única API de enumeración para evitar PowerShell en tiempo de ejecución.
        private bool TryReadScheduledTasksViaCom(List<ScheduledTaskSnapshot> items)
        {
            object serviceObject = null;
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return false;
                serviceObject = Activator.CreateInstance(serviceType);
                dynamic service = serviceObject;
                service.Connect();
                dynamic root = service.GetFolder("\\");
                ReadTaskFolder(root, items);
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudieron revisar tareas mediante Task Scheduler 2.0 COM: " + ex.Message);
                return false;
            }
            finally
            {
                if (serviceObject != null && Marshal.IsComObject(serviceObject))
                {
                    try { Marshal.FinalReleaseComObject(serviceObject); } catch { }
                }
            }
        }

        private void ReadTaskFolder(dynamic folder, List<ScheduledTaskSnapshot> items)
        {
            try
            {
                dynamic tasks = folder.GetTasks(1); // TASK_ENUM_HIDDEN
                int count = Convert.ToInt32(tasks.Count);
                for (int i = 1; i <= count; i++)
                {
                    dynamic task = tasks.Item(i);
                    try
                    {
                        dynamic definition = task.Definition;
                        dynamic triggers = definition.Triggers;
                        bool isLogon = false;
                        int triggerCount = Convert.ToInt32(triggers.Count);
                        for (int t = 1; t <= triggerCount; t++)
                        {
                            dynamic trigger = triggers.Item(t);
                            try
                            {
                                // TASK_TRIGGER_LOGON = 9
                                if (Convert.ToInt32(trigger.Type) == 9) { isLogon = true; break; }
                            }
                            catch { }
                        }
                        if (!isLogon) continue;

                        StringBuilder actionText = new StringBuilder();
                        dynamic actions = definition.Actions;
                        int actionCount = Convert.ToInt32(actions.Count);
                        for (int a = 1; a <= actionCount; a++)
                        {
                            dynamic action = actions.Item(a);
                            try
                            {
                                // TASK_ACTION_EXEC = 0
                                if (Convert.ToInt32(action.Type) != 0) continue;
                                string execute = Convert.ToString(action.Path);
                                string arguments = Convert.ToString(action.Arguments);
                                if (actionText.Length > 0) actionText.Append(" ; ");
                                actionText.Append((execute + " " + arguments).Trim());
                            }
                            catch { }
                        }

                        string full = Convert.ToString(task.Path) ?? string.Empty;
                        int slash = full.LastIndexOf('\\');
                        string path = slash >= 0 ? full.Substring(0, slash + 1) : "\\";
                        string name = slash >= 0 ? full.Substring(slash + 1) : full;
                        items.Add(new ScheduledTaskSnapshot
                        {
                            TaskPath = string.IsNullOrEmpty(path) ? "\\" : path,
                            TaskName = name,
                            Action = actionText.ToString(),
                            Enabled = Convert.ToBoolean(task.Enabled)
                        });
                    }
                    catch { }
                }

                dynamic folders = folder.GetFolders(0);
                int folderCount = Convert.ToInt32(folders.Count);
                for (int i = 1; i <= folderCount; i++)
                {
                    dynamic child = folders.Item(i);
                    try { ReadTaskFolder(child, items); } catch { }
                }
            }
            catch { }
        }

        private static bool LooksLikeUpdaterTask(string text)
        {
            string value = (text ?? string.Empty).ToLowerInvariant();
            return value.Contains("update") || value.Contains("updater") || value.Contains("maintenance") ||
                   value.Contains("install") || value.Contains("uninstall") || value.Contains("telemetry");
        }

        private void InvalidateScheduledTaskCache()
        {
            _scheduledTaskCache = null;
            _scheduledTaskCacheUtc = DateTime.MinValue;
        }

        private void DisableScheduledTask(StartupEntryBackup entry)
        {
            string fullName = ScheduledTaskFullName(entry);
            if (string.IsNullOrWhiteSpace(fullName)) return;
            try
            {
                TaskSchedulerInterop.SetTaskEnabled(fullName, false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No se pudo deshabilitar tarea de inicio " + fullName + " mediante Task Scheduler 2.0: " + ex.Message, ex);
            }
            InvalidateScheduledTaskCache();
            _log.Info("Inicio automático deshabilitado (Task Scheduler API): " + fullName);
        }

        private void RestoreScheduledTask(StartupEntryBackup backup)
        {
            if (backup == null || !backup.TaskWasEnabled) return;
            string fullName = ScheduledTaskFullName(backup);
            if (string.IsNullOrWhiteSpace(fullName)) return;
            try
            {
                TaskSchedulerInterop.SetTaskEnabled(fullName, true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No se pudo restaurar tarea de inicio " + fullName + " mediante Task Scheduler 2.0: " + ex.Message, ex);
            }
            InvalidateScheduledTaskCache();
            _log.Info("Inicio automático restaurado (Task Scheduler API): " + fullName);
        }

        private static string ScheduledTaskFullName(StartupEntryBackup entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.TaskName)) return null;
            string path = entry.TaskPath ?? "\\";
            if (!path.StartsWith("\\", StringComparison.Ordinal)) path = "\\" + path;
            if (!path.EndsWith("\\", StringComparison.Ordinal)) path += "\\";
            return path + entry.TaskName;
        }

        private static string DecodeBase64Utf8(string text)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(text ?? string.Empty)); }
            catch { return string.Empty; }
        }

        private void ScanStartupFolders(StartupRule rule, List<StartupEntryBackup> results)
        {
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StartupEntryBackup existing in results)
                if (!string.IsNullOrWhiteSpace(existing.FilePath)) seen.Add(existing.FilePath);

            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder); }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo revisar carpeta Inicio " + folder + ": " + ex.Message);
                    continue;
                }

                foreach (string file in files)
                {
                    try
                    {
                        if (!StartupFileMatches(file, rule)) continue;
                        string full = Path.GetFullPath(file);
                        if (!seen.Add(full)) continue;

                        StartupEntryBackup backup = new StartupEntryBackup();
                        backup.EntryType = "File";
                        backup.FilePath = full;
                        backup.ValueName = Path.GetFileName(file);
                        backup.ValueData = full;
                        backup.BackupPath = Path.Combine(AppPaths.Backups, "Startup", Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(file));
                        results.Add(backup);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("No se pudo analizar entrada de Inicio " + file + ": " + ex.Message);
                    }
                }
            }
        }

        private static bool StartupFileMatches(string file, StartupRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.MatchText)) return false;
            string name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
            if (rule.SearchValueName && name.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!rule.SearchValueData) return false;

            string ext = Path.GetExtension(file);
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                CustomAppDetectionResult detected = ShortcutResolver.Detect(file);
                if (detected != null && detected.Success)
                {
                    if (!string.IsNullOrWhiteSpace(detected.ProcessName) && detected.ProcessName.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (!string.IsNullOrWhiteSpace(detected.LaunchTargetPath) && detected.LaunchTargetPath.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return file.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RemoveStartupFile(StartupEntryBackup entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath)) return;
            if (string.IsNullOrWhiteSpace(entry.BackupPath))
                throw new InvalidOperationException("No hay ruta de backup para " + entry.FilePath);

            string directory = Path.GetDirectoryName(entry.BackupPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(entry.BackupPath)) File.Delete(entry.BackupPath);
            File.Copy(entry.FilePath, entry.BackupPath, true);
            MachineDataSecurity.ProtectFile(entry.BackupPath);
            File.Delete(entry.FilePath);
            _log.Info("Inicio automático eliminado (carpeta Inicio): " + entry.FilePath);
        }

        private void RestoreStartupFile(StartupEntryBackup backup)
        {
            if (backup == null || string.IsNullOrWhiteSpace(backup.FilePath)) return;
            if (File.Exists(backup.FilePath))
            {
                _log.Info("Entrada de Inicio ya existe al restaurar: " + backup.FilePath);
                return;
            }
            if (string.IsNullOrWhiteSpace(backup.BackupPath) || !File.Exists(backup.BackupPath))
                throw new InvalidOperationException("No se encontró la copia de la entrada de Inicio: " + backup.ValueName);

            string directory = Path.GetDirectoryName(backup.FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.Copy(backup.BackupPath, backup.FilePath, true);
            File.Delete(backup.BackupPath);
            _log.Info("Inicio automático restaurado (carpeta Inicio): " + backup.FilePath);
        }

        private sealed class ScheduledTaskSnapshot
        {
            public string TaskPath { get; set; }
            public string TaskName { get; set; }
            public string Action { get; set; }
            public bool Enabled { get; set; }
        }

        private static readonly string[] StartupApprovedRunKeys = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
        };

        private void CaptureStartupApprovals(StartupEntryBackup backup, RegistryHive hive, RegistryView view)
        {
            if (backup == null || string.IsNullOrWhiteSpace(backup.ValueName)) return;
            if (string.IsNullOrWhiteSpace(backup.KeyPath) ||
                !backup.KeyPath.EndsWith(@"\Run", StringComparison.OrdinalIgnoreCase)) return;
            if (backup.StartupApprovals == null) backup.StartupApprovals = new List<StartupApprovalBackup>();

            foreach (string path in StartupApprovedRunKeys)
            {
                StartupApprovalBackup approval = new StartupApprovalBackup();
                approval.Hive = backup.Hive;
                approval.RegistryView = view.ToString();
                approval.KeyPath = path;
                approval.ValueName = backup.ValueName;
                try
                {
                    using (RegistryKey baseKey = OpenHiveForEntry(hive, view, false))
                    using (RegistryKey key = baseKey == null ? null : baseKey.OpenSubKey(path, false))
                    {
                        if (key != null)
                        {
                            object value = key.GetValue(backup.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            if (value != null)
                            {
                                approval.Exists = true;
                                RegistryValueKind kind = key.GetValueKind(backup.ValueName);
                                approval.ValueKind = kind.ToString();
                                if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None) approval.BinaryData = value as byte[];
                                else if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord) approval.IntegerData = Convert.ToInt64(value);
                                else approval.StringData = Convert.ToString(value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo capturar StartupApproved para " + backup.ValueName + " en " + path + ": " + ex.Message);
                }
                backup.StartupApprovals.Add(approval);
            }
        }

        private static bool IsDisabledByStartupApproval(StartupEntryBackup backup)
        {
            if (backup == null || backup.StartupApprovals == null) return false;
            foreach (StartupApprovalBackup approval in backup.StartupApprovals)
            {
                if (approval == null || !approval.Exists) continue;
                byte[] data = approval.BinaryData;
                // Windows usa 0x03 como estado deshabilitado en StartupApproved.
                // Solo tratamos ese patrón inequívoco; ausencia/otros valores se dejan activos.
                if (data != null && data.Length > 0 && data[0] == 0x03) return true;
            }
            return false;
        }

        private void RestoreStartupApprovals(StartupEntryBackup backup)
        {
            if (backup == null || backup.StartupApprovals == null || backup.StartupApprovals.Count == 0) return;
            foreach (StartupApprovalBackup approval in backup.StartupApprovals)
            {
                RegistryView view = ParseView(approval.RegistryView);
                RegistryHive hive = string.Equals(approval.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                using (RegistryKey baseKey = OpenHiveForEntry(hive, view, true))
                {
                    if (baseKey == null) throw new InvalidOperationException("No se pudo abrir el hive para restaurar StartupApproved de " + approval.ValueName);
                    if (!approval.Exists)
                    {
                        using (RegistryKey key = baseKey.OpenSubKey(approval.KeyPath, true))
                        {
                            if (key != null) key.DeleteValue(approval.ValueName, false);
                        }
                        _log.Info("StartupApproved restaurado a ausencia: " + approval.KeyPath + " [" + approval.ValueName + "]");
                        continue;
                    }
                    using (RegistryKey key = baseKey.CreateSubKey(approval.KeyPath))
                    {
                        if (key == null) throw new InvalidOperationException("No se pudo abrir/crear StartupApproved para " + approval.ValueName);
                        RegistryValueKind kind = RegistryValueKind.Binary;
                        try { kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), approval.ValueKind, true); } catch { }
                        object value;
                        if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None) value = approval.BinaryData ?? new byte[0];
                        else if (kind == RegistryValueKind.DWord) value = Convert.ToInt32(approval.IntegerData);
                        else if (kind == RegistryValueKind.QWord) value = approval.IntegerData;
                        else value = approval.StringData ?? string.Empty;
                        key.SetValue(approval.ValueName, value, kind);
                    }
                    _log.Info("StartupApproved restaurado: " + approval.KeyPath + " [" + approval.ValueName + "]");
                }
            }
        }

        private bool StartupApprovalsMatch(StartupEntryBackup backup, out string detail)
        {
            detail = "entrada Run restaurada";
            if (backup == null || backup.StartupApprovals == null || backup.StartupApprovals.Count == 0) return true;
            foreach (StartupApprovalBackup expected in backup.StartupApprovals)
            {
                RegistryView view = ParseView(expected.RegistryView);
                RegistryHive hive = string.Equals(expected.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                using (RegistryKey baseKey = OpenHiveForEntry(hive, view, false))
                using (RegistryKey key = baseKey == null ? null : baseKey.OpenSubKey(expected.KeyPath, false))
                {
                    object value = key == null ? null : key.GetValue(expected.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    bool exists = value != null;
                    if (exists != expected.Exists)
                    {
                        detail = "StartupApproved no coincide en " + expected.KeyPath;
                        return false;
                    }
                    if (!exists) continue;
                    RegistryValueKind kind = key.GetValueKind(expected.ValueName);
                    if (!string.Equals(kind.ToString(), expected.ValueKind ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        detail = "tipo StartupApproved distinto en " + expected.KeyPath;
                        return false;
                    }
                    if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None)
                    {
                        byte[] a = expected.BinaryData ?? new byte[0];
                        byte[] b = value as byte[] ?? new byte[0];
                        if (a.Length != b.Length) { detail = "StartupApproved binario distinto"; return false; }
                        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { detail = "StartupApproved binario distinto"; return false; }
                    }
                    else if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord)
                    {
                        if (Convert.ToInt64(value) != expected.IntegerData) { detail = "StartupApproved numérico distinto"; return false; }
                    }
                    else if (!string.Equals(Convert.ToString(value) ?? string.Empty, expected.StringData ?? string.Empty, StringComparison.Ordinal))
                    {
                        detail = "StartupApproved texto distinto";
                        return false;
                    }
                }
            }
            detail = "Run + StartupApproved restaurados";
            return true;
        }

        private RegistryKey OpenHiveForEntry(RegistryHive hive, RegistryView view, bool writable)
        {
            if (hive == RegistryHive.CurrentUser && !string.IsNullOrWhiteSpace(_userSidOverride))
            {
                RegistryKey users = RegistryKey.OpenBaseKey(RegistryHive.Users, view);
                RegistryKey userRoot = users.OpenSubKey(_userSidOverride, writable);
                users.Dispose();
                if (userRoot == null) throw new InvalidOperationException("El hive del usuario " + _userSidOverride + " no está cargado.");
                return userRoot;
            }
            return RegistryKey.OpenBaseKey(hive, view);
        }

        private static RegistryView ParseView(string text)
        {
            try { return (RegistryView)Enum.Parse(typeof(RegistryView), text, true); }
            catch { return RegistryView.Default; }
        }

        private static string ScheduledTaskFullName(string taskPath, string taskName)
        {
            string path = taskPath ?? "\\";
            if (!path.StartsWith("\\", StringComparison.Ordinal)) path = "\\" + path;
            if (!path.EndsWith("\\", StringComparison.Ordinal)) path += "\\";
            return path + (taskName ?? string.Empty);
        }

        private void ScanHive(RegistryHive hive, string hiveText, StartupRule rule, List<StartupEntryBackup> results)
        {
            RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    {
                        foreach (string runKey in RunKeys)
                        {
                            using (RegistryKey key = baseKey.OpenSubKey(runKey, false))
                            {
                                if (key == null) continue;
                                string[] names = key.GetValueNames();
                                foreach (string name in names)
                                {
                                    object obj = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                    string data = obj == null ? string.Empty : Convert.ToString(obj);
                                    bool match = (rule.SearchValueName && name.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                 (rule.SearchValueData && data.IndexOf(rule.MatchText, StringComparison.OrdinalIgnoreCase) >= 0);
                                    if (!match) continue;

                                    // V1.1.2.12: HKCU no tiene vistas Run independientes de 32/64 bits.
                                    // Incluir RegistryView en la identidad duplicaba la misma entrada en
                                    // diagnóstico, backup y mensajes de restauración. En HKLM sí se conserva
                                    // la vista porque allí 32/64 pueden ser mecanismos realmente distintos.
                                    string identity = hiveText + "|" +
                                        (hive == RegistryHive.CurrentUser ? "USER" : view.ToString()) + "|" +
                                        runKey + "|" + name + "|" + data;
                                    if (!seen.Add(identity)) continue;

                                    StartupEntryBackup backup = new StartupEntryBackup();
                                    backup.Hive = hiveText;
                                    backup.RegistryView = view.ToString();
                                    backup.KeyPath = runKey;
                                    backup.ValueName = name;
                                    backup.ValueData = data;
                                    try { backup.ValueKind = key.GetValueKind(name).ToString(); }
                                    catch { backup.ValueKind = RegistryValueKind.String.ToString(); }
                                    CaptureStartupApprovals(backup, hive, view);
                                    if (IsDisabledByStartupApproval(backup))
                                    {
                                        _log.Info("Entrada Run presente pero deshabilitada por StartupApproved; no se considera inicio activo: " + backup.ValueName);
                                        continue;
                                    }
                                    results.Add(backup);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo revisar inicio automático " + hiveText + "/" + view + ": " + ex.Message);
                }
            }
        }
    }
}
