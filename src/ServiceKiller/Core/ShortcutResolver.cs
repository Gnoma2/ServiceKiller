using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace ServiceKillerV1.Core
{
    public sealed class CustomAppDetectionResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string DisplayName { get; set; }
        public string SourcePath { get; set; }
        public string LaunchTargetPath { get; set; }
        public string ProcessExecutablePath { get; set; }
        public string ProcessName { get; set; }
        public string ShortcutArguments { get; set; }
        public string DetectionNote { get; set; }
        public int RunningInstances { get; set; }
    }

    public static class ShortcutResolver
    {
        public static CustomAppDetectionResult Detect(string sourcePath)
        {
            CustomAppDetectionResult result = new CustomAppDetectionResult();
            result.SourcePath = sourcePath;

            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    result.Error = "El archivo arrastrado no existe.";
                    return result;
                }

                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                string launchTarget;
                string arguments = string.Empty;

                if (ext == ".exe")
                {
                    launchTarget = Path.GetFullPath(sourcePath);
                    result.DisplayName = FriendlyExeName(sourcePath);
                }
                else if (ext == ".lnk")
                {
                    ResolveLnk(sourcePath, out launchTarget, out arguments);
                    result.DisplayName = Path.GetFileNameWithoutExtension(sourcePath);
                }
                else
                {
                    result.Error = "Formato no compatible. Arrastra un acceso directo .lnk o un ejecutable .exe.";
                    return result;
                }

                launchTarget = Environment.ExpandEnvironmentVariables((launchTarget ?? string.Empty).Trim().Trim('"'));
                if (string.IsNullOrWhiteSpace(launchTarget) || !launchTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    result.Error = "El acceso directo no apunta a un ejecutable .exe reconocible. Prueba a arrastrar el .exe real de la aplicación.";
                    return result;
                }

                string processExe = launchTarget;
                string processName = Path.GetFileNameWithoutExtension(processExe);
                string note = "Proceso deducido del ejecutable de destino.";

                string argumentExe = ExtractProcessExe(arguments);
                if (!string.IsNullOrWhiteSpace(argumentExe))
                {
                    processName = Path.GetFileNameWithoutExtension(argumentExe.Trim('"'));
                    string resolvedArgumentPath = ResolveArgumentExePath(argumentExe, launchTarget);
                    processExe = !string.IsNullOrWhiteSpace(resolvedArgumentPath) && File.Exists(resolvedArgumentPath) ? resolvedArgumentPath : string.Empty;
                    note = "El acceso directo usa un launcher/actualizador; el proceso se ha deducido de sus argumentos.";
                }

                if (IsGenericWindowsLauncher(processName) && string.IsNullOrWhiteSpace(argumentExe))
                {
                    result.Error = "El acceso directo usa un lanzador genérico de Windows (" + processName + "). No puedo identificar con seguridad qué proceso debe cerrar ServiceKiller. Arrastra el .exe real de la aplicación.";
                    return result;
                }

                string runningPath;
                int running = FindRunningProcess(processName, out runningPath);
                if (running > 0)
                {
                    note += " Se ha confirmado contra " + running + " proceso(s) actualmente en ejecución.";
                    // Para launchers tipo Update.exe no fijamos una ruta versionada descubierta al vuelo:
                    // el nombre del proceso es más resistente a futuras actualizaciones de la aplicación.
                }

                result.Success = true;
                result.LaunchTargetPath = launchTarget;
                result.ProcessExecutablePath = !string.IsNullOrWhiteSpace(processExe) && File.Exists(processExe) &&
                                               string.Equals(Path.GetFileNameWithoutExtension(processExe), processName, StringComparison.OrdinalIgnoreCase)
                                               ? Path.GetFullPath(processExe) : string.Empty;
                result.ProcessName = processName;
                result.ShortcutArguments = arguments ?? string.Empty;
                result.DetectionNote = note;
                result.RunningInstances = running;
                if (string.IsNullOrWhiteSpace(result.DisplayName)) result.DisplayName = processName;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "No se pudo analizar la aplicación: " + ex.Message;
                return result;
            }
        }

        private static void ResolveLnk(string shortcutPath, out string targetPath, out string arguments)
        {
            IShellLinkW link = null;
            try
            {
                link = (IShellLinkW)new ShellLink();
                IPersistFile persist = (IPersistFile)link;
                persist.Load(shortcutPath, 0);

                StringBuilder target = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                StringBuilder args = new StringBuilder(32768);
                link.GetArguments(args, args.Capacity);

                targetPath = target.ToString();
                arguments = args.ToString();
            }
            finally
            {
                if (link != null)
                {
                    try { Marshal.FinalReleaseComObject(link); } catch { }
                }
            }
        }

        private static string ExtractProcessExe(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return string.Empty;

            Match preferred = Regex.Match(arguments,
                "--(?:processStart|process-start|processStartAndWait)\\s+(?<exe>\"[^\"]+\\.exe\"|[^\\s\"]+\\.exe)",
                RegexOptions.IgnoreCase);
            if (preferred.Success) return preferred.Groups["exe"].Value.Trim().Trim('"');

            return string.Empty;
        }

        private static string ResolveArgumentExePath(string exeText, string launchTarget)
        {
            string clean = Environment.ExpandEnvironmentVariables((exeText ?? string.Empty).Trim().Trim('"'));
            if (Path.IsPathRooted(clean) && File.Exists(clean)) return Path.GetFullPath(clean);

            try
            {
                string dir = Path.GetDirectoryName(launchTarget);
                string candidate = Path.Combine(dir ?? string.Empty, clean);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
            return clean;
        }

        private static int FindRunningProcess(string processName, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(processName)) return 0;
            int count = 0;
            Process[] processes = Process.GetProcessesByName(processName);
            foreach (Process process in processes)
            {
                try
                {
                    count++;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        try { path = process.MainModule == null ? string.Empty : process.MainModule.FileName; }
                        catch { }
                    }
                }
                finally { process.Dispose(); }
            }
            return count;
        }

        private static string FriendlyExeName(string exePath)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                string text = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exePath);
        }

        private static bool IsGenericWindowsLauncher(string processName)
        {
            return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processName, "rundll32", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processName, "cmd", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processName, "powershell", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processName, "pwsh", StringComparison.OrdinalIgnoreCase);
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
