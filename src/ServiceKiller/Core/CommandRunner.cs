using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ServiceKillerV1.Core
{
    public sealed class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool TimedOut { get; set; }
        public bool Success { get { return ExitCode == 0 && !TimedOut; } }
    }

    public static class CommandRunner
    {
        // V1.1.2.7: el timeout se aplica MIENTRAS el proceso está vivo. Las versiones
        // anteriores llamaban ReadToEnd() antes de WaitForExit(timeout), por lo que un
        // hijo que no cerraba stdout/stderr podía bloquear la GUI indefinidamente.
        public static CommandResult Run(string fileName, string arguments, int timeoutMs)
        {
            if (timeoutMs <= 0) timeoutMs = 10000;

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Encoding consoleEncoding;
            try { consoleEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { consoleEncoding = Encoding.Default; }
            psi.StandardOutputEncoding = consoleEncoding;
            psi.StandardErrorEncoding = consoleEncoding;

            Process process = new Process();
            process.StartInfo = psi;
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            object outputLock = new object();
            object errorLock = new object();

            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (outputLock)
                {
                    if (output.Length > 0) output.AppendLine();
                    output.Append(e.Data);
                }
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (errorLock)
                {
                    if (error.Length > 0) error.AppendLine();
                    error.Append(e.Data);
                }
            };

            try
            {
                if (!process.Start())
                    return new CommandResult { ExitCode = -1, Output = string.Empty, Error = "No se pudo iniciar " + fileName };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    int pid = 0;
                    try { pid = process.Id; } catch { }
                    KillProcessTree(process);
                    try { process.WaitForExit(3000); } catch { }

                    string partialOutput;
                    string partialError;
                    lock (outputLock) partialOutput = output.ToString();
                    lock (errorLock) partialError = error.ToString();
                    string timeoutText = "Timeout tras " + timeoutMs + " ms" + (pid > 0 ? " (PID " + pid + ")" : string.Empty) + ".";
                    if (!string.IsNullOrWhiteSpace(partialError)) timeoutText += " " + partialError.Trim();
                    return new CommandResult
                    {
                        ExitCode = -1,
                        TimedOut = true,
                        Output = partialOutput,
                        Error = timeoutText
                    };
                }

                // No usamos WaitForExit() sin timeout para "drenar" los eventos: si un
                // descendiente heredó los handles de stdout/stderr, ese segundo wait podría
                // volver a ser infinito aunque el proceso principal ya haya terminado.
                // Una espera breve permite recoger las últimas líneas sin sacrificar el límite.
                System.Threading.Thread.Sleep(40);

                string finalOutput;
                string finalError;
                lock (outputLock) finalOutput = output.ToString();
                lock (errorLock) finalError = error.ToString();
                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    TimedOut = false,
                    Output = finalOutput,
                    Error = finalError
                };
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    TimedOut = false,
                    Output = output.ToString(),
                    Error = ex.Message
                };
            }
            finally
            {
                try { process.CancelOutputRead(); } catch { }
                try { process.CancelErrorRead(); } catch { }
                process.Dispose();
            }
        }

        private static void KillProcessTree(Process process)
        {
            if (process == null) return;
            try { if (process.HasExited) return; } catch { }

            // V1.1.2.14+: los comandos auxiliares de ServiceKiller ya no lanzan
            // intérpretes complejos. Ante timeout cerramos directamente el proceso
            // iniciado, evitando invocar una segunda herramienta externa de terminación.
            try { process.Kill(); } catch { }
        }
    }
}
