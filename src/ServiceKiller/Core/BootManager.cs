using System;
using System.IO;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class BootManager
    {
        private readonly Logger _log;

        public BootManager(Logger log)
        {
            _log = log;
        }

        public bool NeedsChange(BootTarget target, BootValueBackup backup)
        {
            return !(backup.Exists && string.Equals(backup.Value, target.TargetValue, StringComparison.OrdinalIgnoreCase));
        }

        public void Set(BootTarget target)
        {
            CommandResult result = CommandRunner.Run(BcdEditPath(), "/set {current} " + target.Name + " " + target.TargetValue, 10000);
            if (!result.Success)
                throw new InvalidOperationException("BCDEdit falló al establecer " + target.Name + ": " + (result.Error + " " + result.Output).Trim());

            _log.Info("BCD aplicado: " + target.Name + "=" + target.TargetValue);
        }

        public void Restore(BootValueBackup backup)
        {
            CommandResult result;
            if (backup.Exists)
                result = CommandRunner.Run(BcdEditPath(), "/set {current} " + backup.Name + " " + backup.Value, 10000);
            else
                result = CommandRunner.Run(BcdEditPath(), "/deletevalue {current} " + backup.Name, 10000);

            if (!result.Success)
            {
                string combined = (result.Error + " " + result.Output).Trim();
                // /deletevalue puede devolver error si el elemento ya no existe; no es fatal al restaurar ausencia.
                if (!backup.Exists && (combined.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || combined.IndexOf("introuvable", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _log.Info("BCD restaurado a ausencia: " + backup.Name);
                    return;
                }
                throw new InvalidOperationException("BCDEdit falló al restaurar " + backup.Name + ": " + combined);
            }
            _log.Info("BCD restaurado: " + backup.Name + (backup.Exists ? "=" + backup.Value : " (ausente)"));
        }

        public BootValueBackup Capture(string name)
        {
            BootValueBackup backup = new BootValueBackup();
            backup.Name = name;
            CommandResult result = CommandRunner.Run(BcdEditPath(), "/enum {current}", 10000);
            if (!result.Success) return backup;

            using (StringReader reader = new StringReader(result.Output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed.Substring(name.Length).Trim();
                        backup.Exists = true;
                        backup.Value = value;
                        break;
                    }
                }
            }
            return backup;
        }


        private static string BcdEditPath()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string path;
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                path = Path.Combine(windows, "Sysnative", "bcdedit.exe");
            else
                path = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

            if (!File.Exists(path))
                throw new FileNotFoundException("No se encontró BCDEdit en la ruta de sistema esperada.", path);
            return path;
        }

        public string Describe(string name)
        {
            if (!PrivilegeHelper.IsAdministrator())
                return "Requiere admin para lectura BCD completa";
            BootValueBackup backup = Capture(name);
            return backup.Exists ? backup.Value : "Predeterminado (sin valor explícito)";
        }
    }
}
