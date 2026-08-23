using System;
using Microsoft.Win32;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class RegistryManager
    {
        private readonly Logger _log;
        private readonly string _userSidOverride;

        public RegistryManager(Logger log)
            : this(log, null)
        {
        }

        // userSidOverride permite al restaurador automático apuntar de forma explícita
        // al perfil que creó el journal. HKCU es un alias de HKEY_USERS\<SID>; usar
        // el SID evita cualquier ambigüedad de contexto durante la restauración.
        public RegistryManager(Logger log, string userSidOverride)
        {
            _log = log;
            _userSidOverride = userSidOverride;
        }

        public RegistryValueBackup CaptureDword(RegistryDwordTarget target)
        {
            using (RegistryKey baseKey = OpenBaseKey(target.Hive, false))
            {
                return Capture(baseKey, target);
            }
        }

        public bool NeedsDwordChange(RegistryDwordTarget target, RegistryValueBackup backup)
        {
            return !(backup.Exists && backup.Kind == RegistryValueKind.DWord.ToString() && backup.IntegerData == target.TargetValue);
        }

        public RegistryValueBackup CaptureString(RegistryStringTarget target)
        {
            using (RegistryKey baseKey = OpenBaseKey(target.Hive, false))
            {
                RegistryValueBackup backup = new RegistryValueBackup();
                backup.Hive = target.Hive;
                backup.KeyPath = target.KeyPath;
                backup.ValueName = target.ValueName;
                using (RegistryKey key = baseKey.OpenSubKey(target.KeyPath, false))
                {
                    if (key == null) return backup;
                    object value = key.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (value == null) return backup;
                    backup.Exists = true;
                    RegistryValueKind kind = key.GetValueKind(target.ValueName);
                    backup.Kind = kind.ToString();
                    backup.StringData = Convert.ToString(value);
                }
                return backup;
            }
        }

        public bool NeedsStringChange(RegistryStringTarget target, RegistryValueBackup backup)
        {
            return !(backup.Exists && backup.Kind == RegistryValueKind.String.ToString() &&
                     string.Equals(backup.StringData ?? string.Empty, target.TargetValue ?? string.Empty, StringComparison.Ordinal));
        }

        public void SetString(RegistryStringTarget target)
        {
            using (RegistryKey baseKey = OpenBaseKey(target.Hive, true))
            using (RegistryKey key = baseKey.CreateSubKey(target.KeyPath))
            {
                if (key == null) throw new InvalidOperationException("No se pudo abrir/crear " + target.Hive + "\\" + target.KeyPath);
                key.SetValue(target.ValueName, target.TargetValue ?? string.Empty, RegistryValueKind.String);
            }
            RegistryValueBackup verify = CaptureString(target);
            if (!verify.Exists || verify.Kind != RegistryValueKind.String.ToString() ||
                !string.Equals(verify.StringData ?? string.Empty, target.TargetValue ?? string.Empty, StringComparison.Ordinal))
                throw new InvalidOperationException("Windows no confirmó el REG_SZ escrito en " + target.Hive + "\\" + target.KeyPath + " [" + target.ValueName + "].");
            _log.Info("Registro aplicado: " + target.Hive + "\\" + target.KeyPath + " [" + target.ValueName + "]=\"" + (target.TargetValue ?? string.Empty) + "\"");
        }

        public void SetDword(RegistryDwordTarget target)
        {
            string fullKey = target.Hive + "\\" + target.KeyPath;
            try
            {
                using (RegistryKey baseKey = OpenBaseKey(target.Hive, true))
                using (RegistryKey key = baseKey.CreateSubKey(target.KeyPath))
                {
                    if (key == null)
                        throw new InvalidOperationException("No se pudo abrir/crear " + fullKey);
                    key.SetValue(target.ValueName, target.TargetValue, RegistryValueKind.DWord);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("Windows denegó la escritura de " + fullKey + " [" + target.ValueName + "]. ServiceKiller no cambia ACL ni crea tareas SYSTEM para forzarla.", ex);
            }

            RegistryValueBackup verify = CaptureDword(target);
            if (!verify.Exists || verify.Kind != RegistryValueKind.DWord.ToString() || verify.IntegerData != target.TargetValue)
                throw new InvalidOperationException("Windows no confirmó el DWORD escrito en " + fullKey + " [" + target.ValueName + "].");

            _log.Info("Registro aplicado mediante API Microsoft.Win32: " + fullKey + " [" + target.ValueName + "]=" + target.TargetValue);
        }

        public void Restore(RegistryValueBackup backup)
        {
            if (backup == null) return;

            using (RegistryKey baseKey = OpenBaseKey(backup.Hive, true))
            using (RegistryKey key = baseKey.CreateSubKey(backup.KeyPath))
            {
                if (key == null) throw new InvalidOperationException("No se pudo abrir/crear la clave al restaurar " + backup.KeyPath);
                if (!backup.Exists)
                {
                    key.DeleteValue(backup.ValueName, false);
                }
                else
                {
                    RegistryValueKind kind = ParseKind(backup.Kind);
                    object value;
                    if (kind == RegistryValueKind.DWord) value = Convert.ToInt32(backup.IntegerData);
                    else if (kind == RegistryValueKind.QWord) value = backup.IntegerData;
                    else if (kind == RegistryValueKind.MultiString) value = backup.StringArrayData ?? new string[0];
                    else if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None) value = backup.BinaryData ?? new byte[0];
                    else value = backup.StringData ?? string.Empty;
                    key.SetValue(backup.ValueName, value, kind);
                }
            }
            _log.Info("Registro restaurado: " + backup.Hive + "\\" + backup.KeyPath + " [" + backup.ValueName + "]");
        }

        public bool MatchesBackup(RegistryValueBackup expected, out string detail)
        {
            detail = string.Empty;
            if (expected == null) { detail = "backup nulo"; return false; }
            try
            {
                using (RegistryKey baseKey = OpenBaseKey(expected.Hive, false))
                using (RegistryKey key = baseKey.OpenSubKey(expected.KeyPath, false))
                {
                    object value = key == null ? null : key.GetValue(expected.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    bool exists = value != null;
                    if (exists != expected.Exists) { detail = exists ? "valor presente y debía estar ausente" : "valor ausente y debía existir"; return false; }
                    if (!exists) { detail = "ausente como en el backup"; return true; }
                    RegistryValueKind kind = key.GetValueKind(expected.ValueName);
                    if (!string.Equals(kind.ToString(), expected.Kind ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { detail = "tipo distinto"; return false; }
                    if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord)
                    {
                        bool ok = Convert.ToInt64(value) == expected.IntegerData; detail = ok ? "coincide" : "valor numérico distinto"; return ok;
                    }
                    if (kind == RegistryValueKind.MultiString)
                    {
                        string[] a = expected.StringArrayData ?? new string[0]; string[] b = value as string[] ?? new string[0];
                        if (a.Length != b.Length) { detail = "MultiString distinto"; return false; }
                        for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) { detail = "MultiString distinto"; return false; }
                        detail = "coincide"; return true;
                    }
                    if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None)
                    {
                        byte[] a = expected.BinaryData ?? new byte[0]; byte[] b = value as byte[] ?? new byte[0];
                        if (a.Length != b.Length) { detail = "binario distinto"; return false; }
                        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { detail = "binario distinto"; return false; }
                        detail = "coincide"; return true;
                    }
                    bool textOk = string.Equals(Convert.ToString(value) ?? string.Empty, expected.StringData ?? string.Empty, StringComparison.Ordinal);
                    detail = textOk ? "coincide" : "texto distinto"; return textOk;
                }
            }
            catch (Exception ex) { detail = ex.Message; return false; }
        }

        public string DescribeDword(RegistryDwordTarget target)
        {
            using (RegistryKey baseKey = OpenBaseKey(target.Hive, false))
            using (RegistryKey key = baseKey.OpenSubKey(target.KeyPath, false))
            {
                if (key == null) return "No configurado";
                object value = key.GetValue(target.ValueName, null);
                if (value == null) return "No configurado";
                return target.ValueName + "=" + Convert.ToString(value);
            }
        }

        public string DescribeString(RegistryStringTarget target)
        {
            RegistryValueBackup backup = CaptureString(target);
            if (!backup.Exists) return "No configurado";
            return target.ValueName + "=\"" + (backup.StringData ?? string.Empty) + "\"";
        }

        private static RegistryValueBackup Capture(RegistryKey baseKey, RegistryDwordTarget target)
        {
            RegistryValueBackup backup = new RegistryValueBackup();
            backup.Hive = target.Hive;
            backup.KeyPath = target.KeyPath;
            backup.ValueName = target.ValueName;

            using (RegistryKey key = baseKey.OpenSubKey(target.KeyPath, false))
            {
                if (key == null) return backup;
                object value = key.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null) return backup;
                backup.Exists = true;
                RegistryValueKind kind = key.GetValueKind(target.ValueName);
                backup.Kind = kind.ToString();
                if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord)
                    backup.IntegerData = Convert.ToInt64(value);
                else if (kind == RegistryValueKind.MultiString)
                    backup.StringArrayData = value as string[];
                else if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None)
                    backup.BinaryData = value as byte[];
                else
                    backup.StringData = Convert.ToString(value);
            }
            return backup;
        }

        private RegistryKey OpenBaseKey(string hive, bool writable)
        {
            if (string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase))
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

            if (string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(_userSidOverride))
                    return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);

                using (RegistryKey users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default))
                {
                    RegistryKey userRoot = users.OpenSubKey(_userSidOverride, writable);
                    if (userRoot == null)
                        throw new InvalidOperationException("El hive del usuario " + _userSidOverride + " no está cargado; se reintentará la restauración en el próximo inicio de sesión.");
                    return userRoot;
                }
            }
            throw new ArgumentException("Hive no soportado: " + hive);
        }

        private static RegistryValueKind ParseKind(string text)
        {
            try { return (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), text, true); }
            catch { return RegistryValueKind.String; }
        }
    }
}
