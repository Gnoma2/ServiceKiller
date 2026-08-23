using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    // V1.1.2.4: verifica componente por componente que una restauración temporal
    // haya devuelto el estado guardado en el journal. El informe queda persistido
    // aunque session-state.json se elimine después de restaurar correctamente.
    public static class RestorationVerifier
    {
        public static string BuildAndSave(ActiveState originalState, string originSid, IEnumerable<string> restoreMessages, string restoreMode)
        {
            string text = Build(originalState, originSid, restoreMessages, restoreMode);
            try
            {
                AppPaths.EnsureMachine();
                File.WriteAllText(AppPaths.LastSessionRestoreReport, text, new UTF8Encoding(false));
                MachineDataSecurity.ProtectFile(AppPaths.LastSessionRestoreReport);
            }
            catch { }
            return text;
        }

        public static string Build(ActiveState originalState, string originSid, IEnumerable<string> restoreMessages, string restoreMode)
        {
            Logger log = new Logger();
            WindowsServiceManager services = new WindowsServiceManager(log);
            BootManager boot = new BootManager(log);
            int checkedCount = 0;
            int okCount = 0;
            int failCount = 0;
            StringBuilder detail = new StringBuilder();

            if (originalState == null) originalState = new ActiveState();
            if (originalState.Tweaks == null) originalState.Tweaks = new List<TweakBackup>();

            foreach (TweakBackup tweak in originalState.Tweaks)
            {
                detail.AppendLine();
                detail.AppendLine("[" + (tweak.TweakName ?? tweak.TweakId ?? "Tweak") + "]  ID=" + (tweak.TweakId ?? ""));

                foreach (ServiceBackup expected in tweak.Services)
                {
                    checkedCount++;
                    ServiceBackup actual = services.Capture(expected.Name);
                    bool ok = ServiceMatches(expected, actual);
                    if (ok) okCount++; else failCount++;
                    detail.AppendLine((ok ? "  OK   " : "  FAIL ") + "Servicio " + expected.Name);
                    detail.AppendLine("       Esperado: " + DescribeService(expected));
                    detail.AppendLine("       Actual:   " + DescribeService(actual));
                }

                foreach (RegistryValueBackup expected in tweak.RegistryValues)
                {
                    checkedCount++;
                    RegistryValueBackup actual = CaptureRegistry(expected, originSid);
                    bool ok = RegistryMatches(expected, actual);
                    if (ok) okCount++; else failCount++;
                    detail.AppendLine((ok ? "  OK   " : "  FAIL ") + "Registro " + expected.Hive + "\\" + expected.KeyPath + " [" + expected.ValueName + "]");
                    detail.AppendLine("       Esperado: " + DescribeRegistry(expected));
                    detail.AppendLine("       Actual:   " + DescribeRegistry(actual));
                }

                foreach (StartupEntryBackup expected in tweak.StartupEntries)
                {
                    checkedCount++;
                    string actualText;
                    bool ok = StartupMatches(expected, originSid, out actualText);
                    if (ok) okCount++; else failCount++;
                    detail.AppendLine((ok ? "  OK   " : "  FAIL ") + "Inicio automático " + (expected.ValueName ?? expected.FilePath ?? "entrada"));
                    detail.AppendLine("       Esperado: presente con el valor respaldado");
                    detail.AppendLine("       Actual:   " + actualText);
                }

                foreach (BootValueBackup expected in tweak.BootValues)
                {
                    checkedCount++;
                    BootValueBackup actual = boot.Capture(expected.Name);
                    bool ok = expected.Exists == actual.Exists && (!expected.Exists || string.Equals(expected.Value ?? string.Empty, actual.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (ok) okCount++; else failCount++;
                    detail.AppendLine((ok ? "  OK   " : "  FAIL ") + "BCD " + expected.Name);
                    detail.AppendLine("       Esperado: " + (expected.Exists ? expected.Value : "ausente"));
                    detail.AppendLine("       Actual:   " + (actual.Exists ? actual.Value : "ausente"));
                }
            }

            bool sessionJournalExists = File.Exists(AppPaths.SessionState);
            bool taskExists = false;
            string taskText = "NO";
            try
            {
                taskExists = new SessionRestoreManager(log).TaskExists();
                taskText = taskExists ? "SÍ" : "NO";
            }
            catch (Exception ex)
            {
                taskText = "NO VERIFICABLE: " + ex.Message;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("================ SERVICEKILLER - VERIFICACIÓN DE RESTAURACIÓN ================");
            sb.AppendLine("Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Modo de restauración: " + (restoreMode ?? "desconocido"));
            sb.AppendLine("Equipo: " + Environment.MachineName);
            sb.AppendLine("Cuenta/SID origen: " + (originalState.OriginAccountName ?? originalState.UserName ?? "") + " / " + (originSid ?? ""));
            sb.AppendLine("Tweaks que había en el journal: " + originalState.Tweaks.Count);
            sb.AppendLine("Componentes verificados: " + checkedCount);
            sb.AppendLine("Correctos: " + okCount);
            sb.AppendLine("Fallos: " + failCount);
            sb.AppendLine("session-state.json después: " + (sessionJournalExists ? "PRESENTE / PENDIENTE" : "AUSENTE"));
            sb.AppendLine("Tarea de auto-restauración después: " + taskText);
            sb.AppendLine("RESULTADO DE VERIFICACIÓN: " + (failCount == 0 && !sessionJournalExists && !taskExists ? "OK" : (failCount == 0 ? "SIN FALLOS DE COMPONENTES, PERO QUEDAN PENDIENTES" : "REVISAR FALLOS")));
            sb.AppendLine("===============================================================================");
            sb.Append(detail.ToString());

            if (restoreMessages != null)
            {
                sb.AppendLine();
                sb.AppendLine("MENSAJES DEL MOTOR DE RESTAURACIÓN");
                foreach (string message in restoreMessages) sb.AppendLine("  " + message);
            }

            sb.AppendLine();
            sb.AppendLine("Nota: el estado Running/Stopped de un servicio se compara con el estado exacto guardado antes del boost. La actividad normal de Windows puede iniciar posteriormente servicios por trigger; si eso ocurre después de esta verificación, no implica por sí solo un fallo de restauración.");
            return sb.ToString();
        }

        private static bool ServiceMatches(ServiceBackup expected, ServiceBackup actual)
        {
            if (expected == null || actual == null) return false;
            if (expected.Exists != actual.Exists) return false;
            if (!expected.Exists) return true;
            return expected.StartValue == actual.StartValue &&
                   expected.DelayedAutoStartExists == actual.DelayedAutoStartExists &&
                   (!expected.DelayedAutoStartExists || expected.DelayedAutoStart == actual.DelayedAutoStart) &&
                   expected.WasRunning == actual.WasRunning;
        }

        private static string DescribeService(ServiceBackup state)
        {
            if (state == null || !state.Exists) return "No disponible";
            string start;
            if (state.StartValue == 0) start = "Boot";
            else if (state.StartValue == 1) start = "System";
            else if (state.StartValue == 2 && state.DelayedAutoStartExists && state.DelayedAutoStart == 1) start = "Automático (retrasado)";
            else if (state.StartValue == 2) start = "Automático";
            else if (state.StartValue == 3) start = "Manual";
            else if (state.StartValue == 4) start = "Deshabilitado";
            else start = "Start=" + state.StartValue;
            return start + " / " + (state.WasRunning ? "Ejecutándose" : "Parado") +
                   " / DelayedAutoStart=" + (state.DelayedAutoStartExists ? state.DelayedAutoStart.ToString() : "ausente");
        }

        private static RegistryValueBackup CaptureRegistry(RegistryValueBackup template, string originSid)
        {
            RegistryValueBackup actual = new RegistryValueBackup();
            actual.Hive = template.Hive;
            actual.KeyPath = template.KeyPath;
            actual.ValueName = template.ValueName;
            try
            {
                RegistryHive hive;
                string path = template.KeyPath;
                if (string.Equals(template.Hive, "HKLM", StringComparison.OrdinalIgnoreCase)) hive = RegistryHive.LocalMachine;
                else
                {
                    if (string.IsNullOrWhiteSpace(originSid)) hive = RegistryHive.CurrentUser;
                    else
                    {
                        hive = RegistryHive.Users;
                        path = originSid + "\\" + template.KeyPath;
                    }
                }

                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                using (RegistryKey key = baseKey.OpenSubKey(path, false))
                {
                    if (key == null) return actual;
                    object value = key.GetValue(template.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (value == null) return actual;
                    actual.Exists = true;
                    RegistryValueKind kind = key.GetValueKind(template.ValueName);
                    actual.Kind = kind.ToString();
                    if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord) actual.IntegerData = Convert.ToInt64(value);
                    else if (kind == RegistryValueKind.MultiString) actual.StringArrayData = value as string[];
                    else if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None) actual.BinaryData = value as byte[];
                    else actual.StringData = Convert.ToString(value);
                }
            }
            catch
            {
                actual.Kind = "LECTURA_ERROR";
            }
            return actual;
        }

        private static bool RegistryMatches(RegistryValueBackup expected, RegistryValueBackup actual)
        {
            if (expected.Exists != actual.Exists) return false;
            if (!expected.Exists) return true;
            if (!string.Equals(expected.Kind ?? string.Empty, actual.Kind ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(expected.Kind, RegistryValueKind.DWord.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(expected.Kind, RegistryValueKind.QWord.ToString(), StringComparison.OrdinalIgnoreCase)) return expected.IntegerData == actual.IntegerData;
            if (string.Equals(expected.Kind, RegistryValueKind.MultiString.ToString(), StringComparison.OrdinalIgnoreCase))
                return Enumerable.SequenceEqual(expected.StringArrayData ?? new string[0], actual.StringArrayData ?? new string[0]);
            if (string.Equals(expected.Kind, RegistryValueKind.Binary.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(expected.Kind, RegistryValueKind.None.ToString(), StringComparison.OrdinalIgnoreCase))
                return Enumerable.SequenceEqual(expected.BinaryData ?? new byte[0], actual.BinaryData ?? new byte[0]);
            return string.Equals(expected.StringData ?? string.Empty, actual.StringData ?? string.Empty, StringComparison.Ordinal);
        }

        private static string DescribeRegistry(RegistryValueBackup value)
        {
            if (value == null) return "lectura no disponible";
            if (!value.Exists) return "ausente";
            if (string.Equals(value.Kind, "LECTURA_ERROR", StringComparison.OrdinalIgnoreCase)) return "ERROR DE LECTURA";
            if (string.Equals(value.Kind, RegistryValueKind.DWord.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.Kind, RegistryValueKind.QWord.ToString(), StringComparison.OrdinalIgnoreCase)) return value.Kind + "=" + value.IntegerData;
            if (string.Equals(value.Kind, RegistryValueKind.MultiString.ToString(), StringComparison.OrdinalIgnoreCase)) return value.Kind + "=" + string.Join(" | ", value.StringArrayData ?? new string[0]);
            if (string.Equals(value.Kind, RegistryValueKind.Binary.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.Kind, RegistryValueKind.None.ToString(), StringComparison.OrdinalIgnoreCase)) return value.Kind + " (" + (value.BinaryData == null ? 0 : value.BinaryData.Length) + " bytes)";
            return (value.Kind ?? "String") + "=" + (value.StringData ?? string.Empty);
        }

        private static bool StartupMatches(StartupEntryBackup expected, string originSid, out string actualText)
        {
            actualText = "no encontrado";
            try
            {
                if (string.Equals(expected.EntryType, "File", StringComparison.OrdinalIgnoreCase))
                {
                    bool exists = !string.IsNullOrWhiteSpace(expected.FilePath) && File.Exists(expected.FilePath);
                    actualText = exists ? "presente: " + expected.FilePath : "ausente: " + expected.FilePath;
                    return exists;
                }

                RegistryView view = RegistryView.Default;
                try { view = (RegistryView)Enum.Parse(typeof(RegistryView), expected.RegistryView, true); } catch { }
                RegistryHive hive;
                string path = expected.KeyPath;
                if (string.Equals(expected.Hive, "HKLM", StringComparison.OrdinalIgnoreCase)) hive = RegistryHive.LocalMachine;
                else
                {
                    if (string.IsNullOrWhiteSpace(originSid)) hive = RegistryHive.CurrentUser;
                    else { hive = RegistryHive.Users; path = originSid + "\\" + expected.KeyPath; }
                }
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = baseKey.OpenSubKey(path, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(expected.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (value == null) return false;
                    string data = Convert.ToString(value);
                    string kind = key.GetValueKind(expected.ValueName).ToString();
                    actualText = kind + "=" + data;
                    return string.Equals(data ?? string.Empty, expected.ValueData ?? string.Empty, StringComparison.Ordinal) &&
                           string.Equals(kind ?? string.Empty, expected.ValueKind ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                actualText = "ERROR DE LECTURA: " + ex.Message;
                return false;
            }
        }
    }
}
