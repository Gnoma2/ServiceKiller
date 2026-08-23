using System;
using System.IO;
using System.Text;

namespace ServiceKillerV1.Core
{
    public sealed class Logger
    {
        private readonly object _sync = new object();
        private string _logFilePath;
        public event Action<string> LineWritten;

        public Logger()
        {
            try { AppPaths.EnsureUser(); } catch { }

            if (PrivilegeHelper.IsAdministrator())
            {
                try
                {
                    AppPaths.EnsureMachine();
                    _logFilePath = AppPaths.LogFile;
                }
                catch
                {
                    _logFilePath = AppPaths.UserLogFile;
                }
            }
            else
            {
                _logFilePath = AppPaths.UserLogFile;
            }
        }

        public string LogFilePath { get { return _logFilePath; } }

        public void Info(string message) { Write("INFO", message); }
        public void Warn(string message) { Write("WARN", message); }
        public void Error(string message) { Write("ERROR", message); }

        private void Write(string level, string message)
        {
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", DateTime.Now, level, message);
            lock (_sync)
            {
                try
                {
                    EnsureParent(_logFilePath);
                    File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Si ProgramData dejó de ser escribible a mitad de sesión, el log no debe tumbar la app.
                    try
                    {
                        _logFilePath = AppPaths.UserLogFile;
                        EnsureParent(_logFilePath);
                        File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                    }
                    catch { }
                }
            }

            Action<string> handler = LineWritten;
            if (handler != null) handler(line);
        }

        private static void EnsureParent(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
}
