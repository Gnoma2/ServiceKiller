using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ServiceKillerV1.Core
{
    public sealed class ElevatedActionResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public bool RestartRequired { get; set; }
        public int SelectedActions { get; set; }
        public int AppliedActions { get; set; }
        public int NoChangeActions { get; set; }
        public int SkippedActions { get; set; }
        public int ErrorActions { get; set; }
        public int PersistentChanges { get; set; }
        public int TemporaryActions { get; set; }
        public int ProcessesClosed { get; set; }
        public int ServicesStopped { get; set; }
        public int WindowsServicesStopped { get; set; }
        public long DurationMilliseconds { get; set; }
        public string Message { get; set; }
    }

    public static class ElevationManager
    {
        public static ElevatedActionResult Run(string operation, IEnumerable<string> ids)
        {
            ElevatedActionResult result = new ElevatedActionResult();
            string resultPath = Path.Combine(Path.GetTempPath(), "ServiceKiller-" + Guid.NewGuid().ToString("N") + ".result");
            string idText = string.Join(",", ids == null ? new string[0] : ids.Where(delegate(string x) { return !string.IsNullOrWhiteSpace(x); }).ToArray());

            string args = "--worker " + Quote(operation) +
                          " --ids " + Quote(idText) +
                          " --result " + Quote(resultPath) +
                          " --origin-sid " + Quote(PrivilegeHelper.CurrentUserSid());

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Application.ExecutablePath;
            psi.Arguments = args;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                using (Process process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        result.Message = "Windows no pudo iniciar el proceso elevado.";
                        return result;
                    }
                    process.WaitForExit();
                }

                if (File.Exists(resultPath))
                {
                    string[] lines = File.ReadAllLines(resultPath, Encoding.UTF8);
                    if (lines.Length > 0)
                    {
                        string[] header = lines[0].Split('|');
                        result.Success = header.Length > 0 && string.Equals(header[0], "OK", StringComparison.OrdinalIgnoreCase);
                        result.RestartRequired = header.Length > 1 && header[1] == "1";
                        // V1.03: los workers APPLY devuelven estadísticas estructuradas.
                        // Los workers de restauración anteriores siguen siendo compatibles: campos ausentes = 0.
                        result.SelectedActions = ParseInt(header, 2);
                        result.AppliedActions = ParseInt(header, 3);
                        result.NoChangeActions = ParseInt(header, 4);
                        result.SkippedActions = ParseInt(header, 5);
                        result.ErrorActions = ParseInt(header, 6);
                        result.PersistentChanges = ParseInt(header, 7);
                        result.TemporaryActions = ParseInt(header, 8);
                        result.ProcessesClosed = ParseInt(header, 9);
                        result.ServicesStopped = ParseInt(header, 10);
                        result.DurationMilliseconds = ParseLong(header, 11);
                        result.WindowsServicesStopped = ParseInt(header, 12);
                        result.Message = lines.Length > 1 ? string.Join(Environment.NewLine, lines.Skip(1).ToArray()) : (result.Success ? "Operación completada." : "La operación elevada falló.");
                    }
                }

                if (string.IsNullOrEmpty(result.Message))
                    result.Message = result.Success ? "Operación completada." : "El proceso elevado terminó sin devolver un resultado válido. Revisa el LOG.";
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
                {
                    result.Cancelled = true;
                    result.Message = "La solicitud de permisos de administrador fue cancelada.";
                }
                else
                {
                    result.Message = "No se pudo solicitar elevación: " + ex.Message;
                }
            }
            catch (Exception ex)
            {
                result.Message = "Error al ejecutar la operación elevada: " + ex.Message;
            }
            finally
            {
                try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch { }
            }

            return result;
        }


        private static int ParseInt(string[] parts, int index)
        {
            if (parts == null || index < 0 || index >= parts.Length) return 0;
            int value;
            return int.TryParse(parts[index], out value) ? value : 0;
        }

        private static long ParseLong(string[] parts, int index)
        {
            if (parts == null || index < 0 || index >= parts.Length) return 0L;
            long value;
            return long.TryParse(parts[index], out value) ? value : 0L;
        }
        private static string Quote(string value)
        {
            if (value == null) value = string.Empty;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
