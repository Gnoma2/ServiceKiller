using System;
using System.Windows.Forms;
using ServiceKillerV1.Core;
using ServiceKillerV1.UI;

namespace ServiceKillerV1
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                // Crear primero la ruta de diagnóstico de usuario. No requiere admin.
                try { AppPaths.EnsureUser(); } catch { }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    StartupDiagnostics.Report(e.Exception);
                };

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex != null) StartupDiagnostics.Report(ex);
                };

                // La GUI siempre abre con el token normal. Solo las acciones mutativas
                // relanzan este mismo EXE como worker elevado mediante UAC.
                if (WorkerRunner.IsWorkerRequest(args))
                {
                    Environment.ExitCode = WorkerRunner.Run(args);
                    return;
                }

                // Si la restauración temporal automática ya terminó, el worker protegido
                // puede quedar como archivo inerte porque no es prudente intentar que un
                // EXE se borre a sí mismo mientras se ejecuta. La GUI lo retira aquí
                // sin UAC adicional, pero solo si el worker dejó previamente una marca
                // protegida de limpieza para esta misma cuenta.
                try
                {
                    Logger cleanupLog = new Logger();
                    SessionRestoreManager.TryCleanupCompletedRestoreWorker(cleanupLog);
                }
                catch { }

                Application.Run(new MainForm(PrivilegeHelper.IsAdministrator()));
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Report(ex);
                Environment.ExitCode = 100;
            }
        }
    }
}
