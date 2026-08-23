using System;
using Microsoft.Win32;

namespace ServiceKillerV1.Core
{
    public static class WindowsCompatibility
    {
        public static Version Version
        {
            get
            {
                try { return Environment.OSVersion.Version; }
                catch { return new Version(0, 0); }
            }
        }

        public static bool IsWindows7
        {
            get { Version v = Version; return v.Major == 6 && v.Minor == 1; }
        }

        public static bool IsWindows8OrNewer
        {
            get { Version v = Version; return v.Major > 6 || (v.Major == 6 && v.Minor >= 2); }
        }

        public static bool IsWindows10OrNewer
        {
            get { return Version.Major >= 10; }
        }

        public static bool SupportsModernWidgets
        {
            get
            {
                int build;
                return IsWindows10OrNewer && int.TryParse(BuildNumber, out build) && build >= 22000;
            }
        }

        public static bool SupportsClientHypervisor
        {
            // Las versiones cliente anteriores a Windows 8 no incluyen esta capacidad.
            get { return IsWindows8OrNewer; }
        }

        public static string ProductName
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                    {
                        if (key != null)
                        {
                            string name = Convert.ToString(key.GetValue("ProductName", string.Empty));
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                name = name.Trim();
                                int build;
                                string buildText = Convert.ToString(key.GetValue("CurrentBuildNumber", string.Empty));
                                if (int.TryParse(buildText, out build) && build >= 22000 && name.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                                    name = name.Replace("Windows 10", "Windows 11");
                                return name;
                            }
                        }
                    }
                }
                catch { }
                return Environment.OSVersion.VersionString;
            }
        }

        public static string BuildNumber
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                    {
                        if (key != null)
                        {
                            string value = Convert.ToString(key.GetValue("CurrentBuildNumber", string.Empty));
                            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                        }
                    }
                }
                catch { }
                return Version.Build >= 0 ? Version.Build.ToString() : "?";
            }
        }

        public static string DisplayVersion
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                    {
                        if (key != null)
                        {
                            string value = Convert.ToString(key.GetValue("DisplayVersion", string.Empty));
                            if (string.IsNullOrWhiteSpace(value)) value = Convert.ToString(key.GetValue("ReleaseId", string.Empty));
                            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                        }
                    }
                }
                catch { }
                return string.Empty;
            }
        }

        public static string FriendlyName
        {
            get
            {
                string name = ProductName;
                string display = DisplayVersion;
                string build = BuildNumber;
                return name + (string.IsNullOrWhiteSpace(display) ? string.Empty : " " + display) + " · Build " + build;
            }
        }

        public static string CompatibilitySummary
        {
            get
            {
                int build;
                if (IsWindows10OrNewer && int.TryParse(BuildNumber, out build) && build >= 22000)
                    return "Windows 11: plataforma validada. Referencia de pruebas: Windows 11 Pro 25H2 x64, build 26200.";
                if (IsWindows10OrNewer)
                    return "Windows 10: no validado en esta versión pública.";
                return "Esta versión de Windows no está validada públicamente para ServiceKiller.";
            }
        }
    }
}
