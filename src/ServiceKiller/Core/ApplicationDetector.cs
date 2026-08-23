using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class ApplicationPresenceResult
    {
        public ApplicationInstallState State { get; set; }
        public bool IsRunning { get; set; }
        public string Details { get; set; }
        // V1.03: recursos de la familia de procesos detectada.
        public int ProcessCount { get; set; }
        public int RootProcessCount { get; set; }
        public long MemoryMb { get; set; }
    }

    /// <summary>
    /// V1.03: además de separar instalación/ejecución, mide procesos y RAM asociados.
    /// La detección es deliberadamente conservadora: si faltan pruebas suficientes,
    /// devuelve NO VERIFICABLE en vez de afirmar incorrectamente NO INSTALADO.
    /// </summary>
    public sealed class ApplicationDetector
    {
        private readonly ProcessManager _processes;
        private readonly Logger _log;

        public ApplicationDetector(ProcessManager processes, Logger log)
        {
            _processes = processes;
            _log = log;
        }

        public ApplicationPresenceResult Detect(TweakDefinition tweak)
        {
            ApplicationPresenceResult result = new ApplicationPresenceResult();
            result.State = ApplicationInstallState.NotApplicable;
            result.Details = string.Empty;

            if (tweak == null || !tweak.IsApplication)
                return result;

            ProcessGroupMetrics resourceMetrics = new ProcessGroupMetrics();
            if (tweak.ProcessNames.Count > 0 || tweak.ProcessPrefixes.Count > 0 || tweak.ProcessPaths.Count > 0)
                resourceMetrics = _processes.MeasureForTweak(tweak);

            bool running = resourceMetrics.ProcessCount > 0 || IsRunning(tweak);
            result.IsRunning = running;
            result.ProcessCount = resourceMetrics.ProcessCount;
            result.RootProcessCount = resourceMetrics.RootProcessCount;
            result.MemoryMb = resourceMetrics.WorkingSetMb;
            if (running)
            {
                result.State = ApplicationInstallState.InstalledRunning;
                if (resourceMetrics.ProcessCount > 0)
                    result.Details = "Aplicación ejecutándose: " + resourceMetrics.ProcessCount + " proceso(s) asociados en " + resourceMetrics.RootProcessCount + " árbol(es), RAM aproximada: " + resourceMetrics.WorkingSetMb + " MB.";
                else
                    result.Details = "La aplicación está instalada y se detecta residencia/servicio activo, aunque no se ha podido atribuir RAM a procesos concretos.";
                return result;
            }

            if (tweak.IsCustomApplication)
                return DetectCustom(tweak);

            return DetectBuiltIn(tweak);
        }

        public static string StatusText(ApplicationInstallState state)
        {
            if (state == ApplicationInstallState.InstalledRunning) return "INSTALADO · EJECUTÁNDOSE";
            if (state == ApplicationInstallState.InstalledClosed) return "INSTALADO · CERRADO";
            if (state == ApplicationInstallState.NotInstalled) return "NO INSTALADO";
            if (state == ApplicationInstallState.NotVerifiable) return "NO VERIFICABLE";
            return string.Empty;
        }

        private ApplicationPresenceResult DetectCustom(TweakDefinition tweak)
        {
            ApplicationPresenceResult result = new ApplicationPresenceResult();
            result.IsRunning = false;

            List<string> knownPaths = new List<string>();
            AddPath(knownPaths, tweak.CustomLaunchTargetPath);
            foreach (string path in tweak.ProcessPaths) AddPath(knownPaths, path);

            foreach (string path in knownPaths)
            {
                if (SafeFileExists(path))
                {
                    result.State = ApplicationInstallState.InstalledClosed;
                    result.Details = "Aplicación personalizada confirmada por el ejecutable guardado: " + path + Environment.NewLine + "No se detectan procesos activos.";
                    return result;
                }
            }

            string source = tweak.CustomSourcePath ?? string.Empty;
            bool sourceExists = SafeFileExists(source);
            if (sourceExists)
            {
                string extension = string.Empty;
                try { extension = Path.GetExtension(source); } catch { }

                if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    result.State = ApplicationInstallState.InstalledClosed;
                    result.Details = "Aplicación personalizada confirmada por el ejecutable que se añadió originalmente." + Environment.NewLine + "No se detectan procesos activos.";
                    return result;
                }

                if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        CustomAppDetectionResult refreshed = ShortcutResolver.Detect(source);
                        if (refreshed.Success &&
                            (SafeFileExists(refreshed.ProcessExecutablePath) || SafeFileExists(refreshed.LaunchTargetPath)))
                        {
                            result.State = ApplicationInstallState.InstalledClosed;
                            result.Details = "Aplicación personalizada confirmada resolviendo de nuevo su acceso directo." + Environment.NewLine + "No se detectan procesos activos.";
                            return result;
                        }

                        result.State = ApplicationInstallState.NotVerifiable;
                        result.Details = "El acceso directo sigue existiendo, pero su ejecutable actual no se puede confirmar. ServiceKiller conserva la entrada y no afirma que esté desinstalada.";
                        return result;
                    }
                    catch (Exception ex)
                    {
                        if (_log != null) _log.Info("Detección no verificable para " + tweak.Name + ": " + ex.Message);
                        result.State = ApplicationInstallState.NotVerifiable;
                        result.Details = "El acceso directo existe, pero no se ha podido resolver de forma fiable.";
                        return result;
                    }
                }
            }

            if (knownPaths.Count > 0 || !string.IsNullOrWhiteSpace(source))
            {
                result.State = ApplicationInstallState.NotInstalled;
                result.Details = "No se encuentra el acceso directo/ejecutable guardado y tampoco hay procesos activos. La entrada se conserva por si vuelves a instalar la aplicación.";
                return result;
            }

            result.State = ApplicationInstallState.NotVerifiable;
            result.Details = "ServiceKiller conoce el proceso objetivo, pero no dispone de una ruta fiable para confirmar si la aplicación sigue instalada.";
            return result;
        }

        private ApplicationPresenceResult DetectBuiltIn(TweakDefinition tweak)
        {
            string family = BuiltInFamily(tweak.Id);
            bool installed = false;
            bool registryChecked = false;
            string evidence = string.Empty;

            if (family == "epic")
            {
                installed = AnyFileExists(new string[]
                {
                    CombineSpecial(Environment.SpecialFolder.ProgramFilesX86, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
                    CombineSpecial(Environment.SpecialFolder.ProgramFiles, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")
                }, out evidence);
                if (!installed) installed = FindUninstallDisplayName(new string[] { "Epic Games Launcher" }, false, out registryChecked, out evidence);
            }
            else if (family == "powertoys")
            {
                installed = AnyFileExists(new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"PowerToys\PowerToys.exe"),
                    CombineSpecial(Environment.SpecialFolder.ProgramFiles, @"PowerToys\PowerToys.exe"),
                    CombineSpecial(Environment.SpecialFolder.ProgramFilesX86, @"PowerToys\PowerToys.exe")
                }, out evidence);
                if (!installed) installed = FindUninstallDisplayName(new string[] { "Microsoft PowerToys", "PowerToys" }, true, out registryChecked, out evidence);
            }
            else if (family == "teams")
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string package = Path.Combine(local, @"Packages\MSTeams_8wekyb3d8bbwe");
                string classic = Path.Combine(local, @"Microsoft\Teams\current\Teams.exe");
                if (SafeDirectoryExists(package)) { installed = true; evidence = package; }
                else if (SafeFileExists(classic)) { installed = true; evidence = classic; }
                if (!installed) installed = FindTeamsUninstall(out registryChecked, out evidence);
            }
            else if (family == "rewasd")
            {
                installed = AnyFileExists(new string[]
                {
                    CombineSpecial(Environment.SpecialFolder.ProgramFiles, @"reWASD\reWASD.exe"),
                    CombineSpecial(Environment.SpecialFolder.ProgramFilesX86, @"reWASD\reWASD.exe")
                }, out evidence);
                if (!installed && AnyServiceContains("reWASD", out evidence)) installed = true;
                if (!installed) installed = FindUninstallDisplayName(new string[] { "reWASD" }, true, out registryChecked, out evidence);
            }
            else
            {
                ApplicationPresenceResult unknown = new ApplicationPresenceResult();
                unknown.State = ApplicationInstallState.NotVerifiable;
                unknown.IsRunning = false;
                unknown.Details = "Esta aplicación integrada todavía no tiene una regla de instalación específica.";
                return unknown;
            }

            ApplicationPresenceResult result = new ApplicationPresenceResult();
            result.IsRunning = false;
            if (installed)
            {
                result.State = ApplicationInstallState.InstalledClosed;
                result.Details = "Instalación confirmada" + (string.IsNullOrWhiteSpace(evidence) ? "." : " mediante: " + evidence) + Environment.NewLine + "No se detectan procesos activos.";
                return result;
            }

            // Para las cuatro aplicaciones integradas conocemos varias firmas estables.
            // Si el registro no se pudo consultar y no hay firmas de archivo, preferimos NO VERIFICABLE.
            if (!registryChecked && (family == "epic" || family == "powertoys" || family == "teams" || family == "rewasd"))
            {
                result.State = ApplicationInstallState.NotVerifiable;
                result.Details = "No se encontraron firmas conocidas, pero tampoco se pudo completar toda la comprobación de instalación.";
            }
            else
            {
                result.State = ApplicationInstallState.NotInstalled;
                result.Details = "No se encontraron procesos, archivos, paquete/servicio ni entrada de instalación conocida. La opción queda deshabilitada mientras la aplicación no esté instalada.";
            }
            return result;
        }

        private bool IsRunning(TweakDefinition tweak)
        {
            if (tweak.ProcessNames.Count > 0 || tweak.ProcessPrefixes.Count > 0 || tweak.ProcessPaths.Count > 0 || tweak.TemporaryServiceNameContains.Count > 0)
                return _processes.IsAnyRunning(tweak);

            string family = BuiltInFamily(tweak.Id);
            if (family == "epic") return IsProcessRunning("EpicGamesLauncher") || IsProcessRunning("EpicWebHelper");
            if (family == "powertoys") return IsProcessPrefixRunning("PowerToys");
            if (family == "teams") return IsProcessRunning("ms-teams") || IsProcessRunning("Teams");
            if (family == "rewasd") return IsProcessPrefixRunning("reWASD") || IsServiceRunningContains("reWASD");
            return false;
        }

        private static string BuiltInFamily(string id)
        {
            id = id ?? string.Empty;
            if (id.StartsWith("app.epic.", StringComparison.OrdinalIgnoreCase)) return "epic";
            if (id.StartsWith("app.powertoys.", StringComparison.OrdinalIgnoreCase)) return "powertoys";
            if (id.StartsWith("app.teams.", StringComparison.OrdinalIgnoreCase)) return "teams";
            if (id.StartsWith("app.rewasd.", StringComparison.OrdinalIgnoreCase)) return "rewasd";
            return string.Empty;
        }

        private static bool IsProcessRunning(string name)
        {
            try
            {
                Process[] items = Process.GetProcessesByName(name);
                bool found = items.Length > 0;
                foreach (Process item in items) item.Dispose();
                return found;
            }
            catch { return false; }
        }

        private static bool IsProcessPrefixRunning(string prefix)
        {
            bool found = false;
            Process[] items = Process.GetProcesses();
            foreach (Process item in items)
            {
                try
                {
                    if (item.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) found = true;
                }
                catch { }
                finally { item.Dispose(); }
            }
            return found;
        }

        private static bool IsServiceRunningContains(string text)
        {
            bool found = false;
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController service in services)
                {
                    try
                    {
                        if ((service.ServiceName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || service.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) &&
                            service.Status == ServiceControllerStatus.Running) found = true;
                    }
                    catch { }
                    finally { service.Dispose(); }
                }
            }
            catch { }
            return found;
        }

        private static bool AnyServiceContains(string text, out string evidence)
        {
            evidence = string.Empty;
            bool found = false;
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController service in services)
                {
                    try
                    {
                        if (!found && (service.ServiceName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || service.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            evidence = "servicio " + service.ServiceName;
                            found = true;
                        }
                    }
                    catch { }
                    finally { service.Dispose(); }
                }
            }
            catch { }
            return found;
        }

        private static bool FindTeamsUninstall(out bool registryChecked, out string evidence)
        {
            registryChecked = false;
            evidence = string.Empty;
            List<string> names = ReadUninstallDisplayNames(out registryChecked);
            foreach (string name in names)
            {
                if (string.Equals(name, "Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Microsoft Teams (", StringComparison.OrdinalIgnoreCase))
                {
                    if (name.IndexOf("Meeting Add-in", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (name.IndexOf("Machine-Wide Installer", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    evidence = "registro: " + name;
                    return true;
                }
            }
            return false;
        }

        private static bool FindUninstallDisplayName(string[] needles, bool contains, out bool registryChecked, out string evidence)
        {
            registryChecked = false;
            evidence = string.Empty;
            List<string> names = ReadUninstallDisplayNames(out registryChecked);
            foreach (string name in names)
            {
                foreach (string needle in needles)
                {
                    bool match = contains
                        ? name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                        : string.Equals(name, needle, StringComparison.OrdinalIgnoreCase);
                    if (match)
                    {
                        evidence = "registro: " + name;
                        return true;
                    }
                }
            }
            return false;
        }

        private static List<string> ReadUninstallDisplayNames(out bool anyRegistryReadable)
        {
            List<string> names = new List<string>();
            anyRegistryReadable = false;
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
            RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false))
                        {
                            anyRegistryReadable = true;
                            if (uninstall == null) continue;
                            foreach (string subName in uninstall.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey sub = uninstall.OpenSubKey(subName, false))
                                    {
                                        string display = sub == null ? string.Empty : sub.GetValue("DisplayName") as string;
                                        if (!string.IsNullOrWhiteSpace(display) && !names.Contains(display)) names.Add(display.Trim());
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            return names;
        }

        private static bool AnyFileExists(IEnumerable<string> paths, out string evidence)
        {
            evidence = string.Empty;
            foreach (string path in paths)
            {
                if (SafeFileExists(path))
                {
                    evidence = path;
                    return true;
                }
            }
            return false;
        }

        private static string CombineSpecial(Environment.SpecialFolder folder, string relative)
        {
            try
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrWhiteSpace(root)) return string.Empty;
                return Path.Combine(root, relative);
            }
            catch { return string.Empty; }
        }

        private static void AddPath(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!list.Contains(expanded, StringComparer.OrdinalIgnoreCase)) list.Add(expanded);
        }

        private static bool SafeFileExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && File.Exists(Environment.ExpandEnvironmentVariables(path)); }
            catch { return false; }
        }

        private static bool SafeDirectoryExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && Directory.Exists(Environment.ExpandEnvironmentVariables(path)); }
            catch { return false; }
        }
    }
}
