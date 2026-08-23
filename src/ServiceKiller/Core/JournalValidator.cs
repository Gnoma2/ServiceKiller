using System;
using System.Collections.Generic;
using System.IO;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    // Validación defensiva del journal antes de utilizarlo como instrucciones de
    // restauración. No sustituye a la ACL del almacenamiento; añade una segunda barrera
    // frente a archivos dañados o manipulados.
    internal static class JournalValidator
    {
        private const int MaxTweaks = 256;
        private const int MaxComponentsPerTweak = 256;
        private const int MaxTextLength = 4096;

        public static void ValidateAndNormalize(ActiveState state)
        {
            if (state == null) throw new InvalidDataException("Journal nulo.");
            if (state.Tweaks == null) state.Tweaks = new List<TweakBackup>();
            if (state.Tweaks.Count > MaxTweaks) throw new InvalidDataException("El journal contiene demasiadas entradas.");

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TweakBackup tweak in state.Tweaks)
            {
                if (tweak == null) throw new InvalidDataException("El journal contiene una entrada de tweak nula.");
                RequireText(tweak.TweakId, "TweakId", 256);
                RequireText(tweak.TweakName, "TweakName", MaxTextLength, allowEmpty: true);
                if (!ids.Add(tweak.TweakId)) throw new InvalidDataException("El journal contiene un TweakId duplicado: " + tweak.TweakId);

                if (tweak.Services == null) tweak.Services = new List<ServiceBackup>();
                if (tweak.RegistryValues == null) tweak.RegistryValues = new List<RegistryValueBackup>();
                if (tweak.StartupEntries == null) tweak.StartupEntries = new List<StartupEntryBackup>();
                if (tweak.BootValues == null) tweak.BootValues = new List<BootValueBackup>();

                Limit(tweak.Services.Count, "servicios");
                Limit(tweak.RegistryValues.Count, "valores de Registro");
                Limit(tweak.StartupEntries.Count, "entradas de inicio");
                Limit(tweak.BootValues.Count, "valores BCD");

                foreach (ServiceBackup service in tweak.Services)
                {
                    if (service == null) throw new InvalidDataException("Backup de servicio nulo.");
                    RequireText(service.Name, "servicio", 256);
                }

                foreach (RegistryValueBackup registry in tweak.RegistryValues)
                {
                    if (registry == null) throw new InvalidDataException("Backup de Registro nulo.");
                    ValidateHive(registry.Hive);
                    RequireText(registry.KeyPath, "ruta de Registro", MaxTextLength);
                    RequireText(registry.ValueName, "nombre de valor de Registro", 1024, allowEmpty: true);
                    RequireText(registry.Kind, "tipo de valor de Registro", 128, allowEmpty: true);
                    if (registry.StringData != null && registry.StringData.Length > 1024 * 1024)
                        throw new InvalidDataException("Dato de Registro de tamaño no razonable.");
                    if (registry.BinaryData != null && registry.BinaryData.Length > 4 * 1024 * 1024)
                        throw new InvalidDataException("Dato binario de Registro de tamaño no razonable.");
                    if (registry.StringArrayData != null && registry.StringArrayData.Length > 4096)
                        throw new InvalidDataException("Dato MultiString de tamaño no razonable.");
                }

                foreach (StartupEntryBackup startup in tweak.StartupEntries)
                {
                    if (startup == null) throw new InvalidDataException("Backup de inicio nulo.");
                    if (startup.StartupApprovals == null) startup.StartupApprovals = new List<StartupApprovalBackup>();
                    if (startup.StartupApprovals.Count > MaxComponentsPerTweak)
                        throw new InvalidDataException("Demasiadas entradas StartupApproved en un backup.");

                    string type = startup.EntryType ?? string.Empty;
                    if (string.Equals(type, "File", StringComparison.OrdinalIgnoreCase))
                    {
                        RequireText(startup.FilePath, "archivo de Inicio", MaxTextLength);
                        RequireText(startup.BackupPath, "backup de archivo de Inicio", MaxTextLength);
                    }
                    else if (string.Equals(type, "ScheduledTask", StringComparison.OrdinalIgnoreCase))
                    {
                        RequireText(startup.TaskName, "nombre de tarea", 1024);
                        RequireText(startup.TaskPath, "ruta de tarea", 2048, allowEmpty: true);
                    }
                    else
                    {
                        ValidateHive(startup.Hive);
                        RequireText(startup.KeyPath, "ruta de inicio en Registro", MaxTextLength);
                        RequireText(startup.ValueName, "valor de inicio", 1024);
                    }

                    foreach (StartupApprovalBackup approval in startup.StartupApprovals)
                    {
                        if (approval == null) throw new InvalidDataException("Backup StartupApproved nulo.");
                        ValidateHive(approval.Hive);
                        RequireText(approval.KeyPath, "ruta StartupApproved", MaxTextLength);
                        RequireText(approval.ValueName, "valor StartupApproved", 1024);
                    }
                }

                foreach (BootValueBackup boot in tweak.BootValues)
                {
                    if (boot == null) throw new InvalidDataException("Backup BCD nulo.");
                    RequireText(boot.Name, "nombre BCD", 256);
                    // Catálogo público actual: es el único valor BCD que ServiceKiller modifica.
                    if (!string.Equals(boot.Name, "hypervisorlaunchtype", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("El journal contiene un valor BCD que ServiceKiller no reconoce: " + boot.Name);
                    RequireText(boot.Value, "valor BCD", 256, allowEmpty: true);
                }
            }
        }

        private static void Limit(int count, string label)
        {
            if (count > MaxComponentsPerTweak)
                throw new InvalidDataException("El journal contiene demasiados " + label + " en una sola entrada.");
        }

        private static void ValidateHive(string hive)
        {
            if (!string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Hive de Registro no permitido en el journal: " + (hive ?? "(nulo)"));
        }

        private static void RequireText(string value, string label, int maxLength, bool allowEmpty = false)
        {
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Campo obligatorio vacío en journal: " + label + ".");
            if (value != null && value.Length > maxLength)
                throw new InvalidDataException("Campo demasiado largo en journal: " + label + ".");
        }
    }
}
