using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ServiceKillerV1.Core
{
    public static class StartupDiagnostics
    {
        public static string Report(Exception ex)
        {
            string text = ServiceKillerV1.BuildInfo.DisplayName + " - fallo durante el arranque" + Environment.NewLine +
                          "Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine +
                          "Usuario: " + Environment.UserName + Environment.NewLine +
                          "Equipo: " + Environment.MachineName + Environment.NewLine +
                          "SO: " + WindowsCompatibility.FriendlyName + Environment.NewLine +
                          "Compatibilidad: " + WindowsCompatibility.CompatibilitySummary + Environment.NewLine +
                          "SO 64-bit: " + Environment.Is64BitOperatingSystem + Environment.NewLine +
                          ".NET: " + Environment.Version + Environment.NewLine + Environment.NewLine +
                          ex.ToString();

            string path = null;
            try
            {
                AppPaths.EnsureUser();
                path = Path.Combine(AppPaths.UserLogs, "startup-crash.log");
                File.WriteAllText(path, text, Encoding.UTF8);
            }
            catch
            {
                try
                {
                    path = Path.Combine(Path.GetTempPath(), "ServiceKiller-startup-crash.log");
                    File.WriteAllText(path, text, Encoding.UTF8);
                }
                catch { }
            }

            try
            {
                MessageBox.Show(
                    "ServiceKiller no pudo abrir la interfaz.\r\n\r\n" +
                    ex.GetType().FullName + ": " + ex.Message +
                    (string.IsNullOrEmpty(path) ? "" : "\r\n\r\nDiagnóstico guardado en:\r\n" + path),
                    ServiceKillerV1.BuildInfo.DisplayName + " - error de arranque",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            return path;
        }
    }
}
