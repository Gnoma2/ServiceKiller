using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ServiceKillerV1.Core
{
    // Gestiona la restauración automática del modo "hasta reinicio".
    // La tarea usa la misma cuenta que activó el modo, con "Highest available".
    // Para no dejar una tarea elevada apuntando a Descargas/Escritorio/etc., se ejecuta
    // una copia de la misma build almacenada en el área de máquina protegida.
    public sealed class SessionRestoreManager
    {
        private readonly Logger _log;

        public SessionRestoreManager(Logger log)
        {
            _log = log;
        }

        public bool Prepare(string originAccountName, string originSid)
        {
            return Prepare(originAccountName, originSid, false);
        }

        public bool Prepare(string originAccountName, string originSid, bool preserveExistingWorker)
        {
            if (!PrivilegeHelper.IsAdministrator())
                throw new InvalidOperationException("La programación de la restauración automática requiere administrador.");
            if (string.IsNullOrWhiteSpace(originAccountName) || string.IsNullOrWhiteSpace(originSid))
                throw new InvalidOperationException("No se pudo identificar la cuenta/SID que debe disparar la restauración automática.");

            AppPaths.EnsureMachine();

            string currentExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
                throw new InvalidOperationException("No se pudo localizar el ServiceKiller.exe actual para preparar la restauración.");

            string workerHash;
            if (preserveExistingWorker)
            {
                if (!File.Exists(AppPaths.SessionRestoreExecutable) || !File.Exists(AppPaths.SessionRestoreHash))
                    throw new InvalidDataException("Existe una sesión temporal pendiente, pero falta su restaurador protegido o su huella SHA-256. No se sustituye automáticamente para no mezclar builds de restauración.");

                workerHash = (File.ReadAllText(AppPaths.SessionRestoreHash, Encoding.ASCII) ?? string.Empty).Trim();
                if (workerHash.Length == 0 || !VerifyFileHash(AppPaths.SessionRestoreExecutable, workerHash))
                    throw new InvalidDataException("Existe una sesión temporal pendiente, pero su restaurador protegido no supera la verificación SHA-256. No se reemplaza automáticamente para no mezclar builds de restauración.");
                _log.Info("Se conserva el restaurador protegido de la sesión temporal ya existente: " + AppPaths.SessionRestoreExecutable);
            }
            else
            {
                workerHash = CreateProtectedRestoreWorker(currentExe);
            }

            string arguments = "--worker restore-session-auto --origin-sid \"" + originSid + "\" --worker-sha256 \"" + workerHash + "\"";
            try
            {
                TaskSchedulerInterop.RegisterSessionRestoreTask(
                    AppPaths.SessionTaskName,
                    originAccountName,
                    AppPaths.SessionRestoreExecutable,
                    arguments,
                    "Restaura automáticamente los cambios temporales de ServiceKiller.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No se pudo programar la restauración automática mediante Task Scheduler 2.0: " + ex.Message, ex);
            }

            if (!TaskExists())
                throw new InvalidOperationException("Windows no confirmó la existencia de la tarea de restauración después de registrarla.");

            string taskXml;
            string taskSddl;
            try
            {
                taskXml = TaskSchedulerInterop.GetTaskXml(AppPaths.SessionTaskName);
                taskSddl = TaskSchedulerInterop.GetTaskSecurityDescriptor(AppPaths.SessionTaskName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("La tarea se creó, pero ServiceKiller no pudo volver a leer su definición de seguridad: " + ex.Message, ex);
            }

            string verificationError;
            if (!VerifyTaskDefinition(taskXml, taskSddl, AppPaths.SessionRestoreExecutable, arguments, out verificationError))
            {
                try { TaskSchedulerInterop.DeleteTask(AppPaths.SessionTaskName); } catch { }
                throw new InvalidOperationException("La tarea de restauración creada por Windows no coincide con la definición segura esperada: " + verificationError);
            }

            try
            {
                string sourceInfo = "RestoreWorker=" + AppPaths.SessionRestoreExecutable + Environment.NewLine +
                                    "SHA256=" + workerHash + Environment.NewLine;
                File.WriteAllText(AppPaths.SessionRestoreSourcePath, sourceInfo, Encoding.UTF8);
                MachineDataSecurity.ProtectFile(AppPaths.SessionRestoreSourcePath);

                File.WriteAllText(AppPaths.SessionTaskXml, taskXml ?? string.Empty, Encoding.Unicode);
                MachineDataSecurity.ProtectFile(AppPaths.SessionTaskXml);
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo guardar toda la información diagnóstica de la tarea temporal: " + ex.Message);
            }

            _log.Info("Restauración automática programada para el próximo logon de " + originAccountName +
                      " mediante Task Scheduler 2.0 COM, con la cuenta de origen y privilegio Highest available.");
            _log.Info("La tarea apunta a una copia protegida y verificada por SHA-256: " + AppPaths.SessionRestoreExecutable);
            return true;
        }

        public bool TaskExists()
        {
            try { return TaskSchedulerInterop.TaskExists(AppPaths.SessionTaskName); }
            catch (Exception ex)
            {
                if (IsExpectedTaskMissing(ex)) return false;
                _log.Warn("No se pudo verificar la tarea de restauración automática: " + ex.Message);
                return false;
            }
        }

        public bool RemoveTask()
        {
            try
            {
                bool removed = TaskSchedulerInterop.DeleteTask(AppPaths.SessionTaskName);
                if (removed)
                    _log.Info("Tarea de restauración automática eliminada.");
                else
                    _log.Info("La tarea de restauración automática ya no existía.");
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo eliminar la tarea de restauración automática: " + ex.Message);
                return false;
            }

            if (TaskExists())
            {
                _log.Warn("Windows sigue mostrando la tarea de restauración automática después de solicitar su eliminación.");
                return false;
            }

            try
            {
                if (File.Exists(AppPaths.SessionRestoreSourcePath)) File.Delete(AppPaths.SessionRestoreSourcePath);
                if (File.Exists(AppPaths.SessionTaskXml)) File.Delete(AppPaths.SessionTaskXml);

                // En una restauración manual el worker protegido no está ejecutándose y se
                // puede limpiar. En la restauración automática es el propio proceso actual;
                // Windows mantiene el EXE abierto y se conserva como copia inerte protegida.
                string runningExe = string.Empty;
                try { runningExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location); } catch { }
                bool runningProtectedWorker = PathsEqual(runningExe, AppPaths.SessionRestoreExecutable);
                if (!runningProtectedWorker)
                {
                    if (File.Exists(AppPaths.SessionRestoreExecutable)) File.Delete(AppPaths.SessionRestoreExecutable);
                    if (File.Exists(AppPaths.SessionRestoreConfig)) File.Delete(AppPaths.SessionRestoreConfig);
                    if (File.Exists(AppPaths.SessionRestoreHash)) File.Delete(AppPaths.SessionRestoreHash);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("La tarea se eliminó, pero no se pudieron limpiar todos sus archivos auxiliares: " + ex.Message);
            }
            return true;
        }

        // Tras una restauración automática correcta, el proceso actual ES el worker
        // protegido y no puede garantizar su propio borrado mientras sigue ejecutándose.
        // Se deja una marca protegida y se concede únicamente DELETE a la cuenta de
        // origen sobre esos archivos ya inertes. La siguiente apertura normal de
        // ServiceKiller los elimina sin UAC adicional.
        public bool MarkProtectedWorkerForDeferredCleanup(string originSid)
        {
            try
            {
                if (!PrivilegeHelper.IsAdministrator())
                    throw new UnauthorizedAccessException("Preparar la limpieza diferida requiere administrador.");
                if (string.IsNullOrWhiteSpace(originSid))
                    throw new InvalidOperationException("No hay SID de origen para preparar la limpieza diferida.");
                if (File.Exists(AppPaths.SessionState))
                    throw new InvalidOperationException("No se prepara limpieza diferida mientras exista session-state.json.");
                if (TaskExists())
                    throw new InvalidOperationException("No se prepara limpieza diferida mientras siga existiendo la tarea temporal.");

                string expectedHash = string.Empty;
                if (File.Exists(AppPaths.SessionRestoreHash))
                    expectedHash = (File.ReadAllText(AppPaths.SessionRestoreHash, Encoding.ASCII) ?? string.Empty).Trim();

                string marker = "SID=" + originSid + Environment.NewLine +
                                "SHA256=" + expectedHash + Environment.NewLine +
                                "READY=" + DateTime.UtcNow.ToString("o") + Environment.NewLine;
                File.WriteAllText(AppPaths.SessionRestoreCleanupReady, marker, new UTF8Encoding(false));
                MachineDataSecurity.ProtectFile(AppPaths.SessionRestoreCleanupReady);

                string[] files = new string[]
                {
                    AppPaths.SessionRestoreExecutable,
                    AppPaths.SessionRestoreConfig,
                    AppPaths.SessionRestoreHash,
                    AppPaths.SessionRestoreSourcePath,
                    AppPaths.SessionTaskXml,
                    AppPaths.SessionRestoreCleanupReady
                };
                foreach (string file in files)
                {
                    if (File.Exists(file))
                        MachineDataSecurity.AllowFileDeletionBySid(file, originSid);
                }

                _log.Info("Limpieza diferida del restaurador protegido preparada para la cuenta de origen.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo preparar la limpieza diferida del restaurador protegido: " + ex.Message);
                return false;
            }
        }

        public static bool TryCleanupCompletedRestoreWorker(Logger log)
        {
            try
            {
                // Nunca tocar una sesión que todavía tenga journal.
                if (File.Exists(AppPaths.SessionState)) return false;

                string runningExe = string.Empty;
                try { runningExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location); } catch { }
                if (PathsEqual(runningExe, AppPaths.SessionRestoreExecutable)) return false;

                bool markerExists = File.Exists(AppPaths.SessionRestoreCleanupReady);

                // Compatibilidad de limpieza con versiones anteriores: si se abre elevado y no
                // queda journal ni tarea, puede retirar un worker huérfano antiguo aunque
                // todavía no exista cleanup-ready.txt.
                if (!markerExists)
                {
                    if (!PrivilegeHelper.IsAdministrator()) return false;

                    SessionRestoreManager manager = new SessionRestoreManager(log ?? new Logger());
                    if (manager.TaskExists()) return false;

                    return DeleteCompletedWorkerFiles(log, true);
                }

                string marker = File.ReadAllText(AppPaths.SessionRestoreCleanupReady, Encoding.UTF8);
                string markerSid = ReadMarkerValue(marker, "SID");
                string currentSid = PrivilegeHelper.CurrentUserSid();
                if (string.IsNullOrWhiteSpace(markerSid) ||
                    string.IsNullOrWhiteSpace(currentSid) ||
                    !string.Equals(markerSid, currentSid, StringComparison.OrdinalIgnoreCase))
                {
                    if (log != null) log.Warn("Existe limpieza diferida del restaurador, pero pertenece a otra cuenta de Windows.");
                    return false;
                }

                return DeleteCompletedWorkerFiles(log, false);
            }
            catch (Exception ex)
            {
                if (log != null) log.Warn("No se pudo completar la limpieza diferida del restaurador protegido: " + ex.Message);
                return false;
            }
        }

        private static bool DeleteCompletedWorkerFiles(Logger log, bool elevatedLegacyCleanup)
        {
            string[] files = new string[]
            {
                AppPaths.SessionRestoreExecutable,
                AppPaths.SessionRestoreConfig,
                AppPaths.SessionRestoreHash,
                AppPaths.SessionRestoreSourcePath,
                AppPaths.SessionTaskXml
            };

            bool ok = true;
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception ex)
                {
                    ok = false;
                    if (log != null) log.Warn("No se pudo eliminar resto de restauración temporal '" + Path.GetFileName(file) + "': " + ex.Message);
                }
            }

            // La marca se borra la última. Si algo falló, se conserva para reintentar
            // automáticamente en la próxima apertura.
            if (ok)
            {
                try
                {
                    if (File.Exists(AppPaths.SessionRestoreCleanupReady))
                        File.Delete(AppPaths.SessionRestoreCleanupReady);
                }
                catch (Exception ex)
                {
                    ok = false;
                    if (log != null) log.Warn("No se pudo retirar cleanup-ready.txt: " + ex.Message);
                }
            }

            if (ok && log != null)
            {
                log.Info(elevatedLegacyCleanup
                    ? "Restaurador protegido huérfano de una versión anterior eliminado."
                    : "Restaurador temporal protegido residual eliminado tras restauración completada.");
            }
            return ok;
        }

        private static string ReadMarkerValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return string.Empty;
            string prefix = key + "=";
            string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length).Trim();
            return string.Empty;
        }

        public static string GetProtectedWorkerStatus()
        {
            bool exe = File.Exists(AppPaths.SessionRestoreExecutable);
            bool hash = File.Exists(AppPaths.SessionRestoreHash);
            if (!exe && !hash) return "AUSENTE";
            if (!exe) return "INCOMPLETO · falta el ejecutable protegido";
            if (!hash) return "INCOMPLETO · falta la huella SHA-256";
            try
            {
                string expected = (File.ReadAllText(AppPaths.SessionRestoreHash, Encoding.ASCII) ?? string.Empty).Trim();
                return VerifyFileHash(AppPaths.SessionRestoreExecutable, expected)
                    ? "PRESENTE · SHA-256 OK"
                    : "PRESENTE · SHA-256 NO COINCIDE";
            }
            catch (Exception ex)
            {
                return "NO VERIFICABLE · " + ex.Message;
            }
        }

        public static bool VerifyCurrentWorkerIntegrity(string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256)) return false;
            string runningExe;
            try { runningExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location); }
            catch { return false; }
            if (!PathsEqual(runningExe, AppPaths.SessionRestoreExecutable)) return false;
            return VerifyFileHash(runningExe, expectedSha256);
        }

        public static bool IsUserHiveLoaded(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;
            try
            {
                using (Microsoft.Win32.RegistryKey users = Microsoft.Win32.Registry.Users)
                using (Microsoft.Win32.RegistryKey key = users.OpenSubKey(sid, false))
                    return key != null;
            }
            catch { return false; }
        }

        private string CreateProtectedRestoreWorker(string currentExe)
        {
            string sourceHash = ComputeSha256(currentExe);
            File.Copy(currentExe, AppPaths.SessionRestoreExecutable, true);
            MachineDataSecurity.ProtectFile(AppPaths.SessionRestoreExecutable);

            string sourceConfig = currentExe + ".config";
            if (File.Exists(sourceConfig))
            {
                File.Copy(sourceConfig, AppPaths.SessionRestoreConfig, true);
                MachineDataSecurity.ProtectFile(AppPaths.SessionRestoreConfig);
            }
            else
            {
                try { if (File.Exists(AppPaths.SessionRestoreConfig)) File.Delete(AppPaths.SessionRestoreConfig); } catch { }
            }

            string copiedHash = ComputeSha256(AppPaths.SessionRestoreExecutable);
            if (!string.Equals(sourceHash, copiedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La copia protegida del restaurador no coincide con el ejecutable de origen.");

            File.WriteAllText(AppPaths.SessionRestoreHash, copiedHash + Environment.NewLine, Encoding.ASCII);
            MachineDataSecurity.ProtectFile(AppPaths.SessionRestoreHash);
            _log.Info("Restaurador protegido preparado. SHA-256: " + copiedHash);
            return copiedHash;
        }

        private static bool VerifyTaskDefinition(string xml, string sddl, string expectedExe, string expectedArguments, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(xml)) { error = "XML vacío"; return false; }
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.PreserveWhitespace = false;
                doc.LoadXml(xml);

                string command = NodeText(doc, "//*[local-name()='Actions']/*[local-name()='Exec']/*[local-name()='Command']");
                string arguments = NodeText(doc, "//*[local-name()='Actions']/*[local-name()='Exec']/*[local-name()='Arguments']");
                string runLevel = NodeText(doc, "//*[local-name()='Principals']/*[local-name()='Principal']/*[local-name()='RunLevel']");
                string logonType = NodeText(doc, "//*[local-name()='Principals']/*[local-name()='Principal']/*[local-name()='LogonType']");
                XmlNode logonTrigger = doc.SelectSingleNode("//*[local-name()='Triggers']/*[local-name()='LogonTrigger']");

                if (!PathsEqual(command, expectedExe)) { error = "ruta de acción inesperada: " + command; return false; }
                if (!string.Equals((arguments ?? string.Empty).Trim(), (expectedArguments ?? string.Empty).Trim(), StringComparison.Ordinal)) { error = "argumentos inesperados"; return false; }
                if (!string.Equals(runLevel, "HighestAvailable", StringComparison.OrdinalIgnoreCase)) { error = "RunLevel no es HighestAvailable"; return false; }
                if (!string.Equals(logonType, "InteractiveToken", StringComparison.OrdinalIgnoreCase)) { error = "LogonType no es InteractiveToken"; return false; }
                if (logonTrigger == null) { error = "falta LogonTrigger"; return false; }

                if (string.IsNullOrWhiteSpace(sddl)) { error = "DACL de tarea no legible"; return false; }
                bool hasSystem = sddl.IndexOf(";;;SY)", StringComparison.OrdinalIgnoreCase) >= 0 || sddl.IndexOf("S-1-5-18", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasAdmins = sddl.IndexOf(";;;BA)", StringComparison.OrdinalIgnoreCase) >= 0 || sddl.IndexOf("S-1-5-32-544", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hasSystem || !hasAdmins) { error = "DACL sin SYSTEM/Administradores esperados"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string NodeText(XmlDocument doc, string xpath)
        {
            XmlNode node = doc.SelectSingleNode(xpath);
            return node == null ? string.Empty : (node.InnerText ?? string.Empty).Trim();
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) sb.Append(value.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool VerifyFileHash(string path, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || string.IsNullOrWhiteSpace(expectedSha256)) return false;
            try { return string.Equals(ComputeSha256(path), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsExpectedTaskMissing(Exception ex)
        {
            const int ErrorFileNotFoundHResult = unchecked((int)0x80070002);
            Exception current = ex;
            while (current != null)
            {
                if (current.HResult == ErrorFileNotFoundHResult) return true;
                current = current.InnerException;
            }
            return false;
        }
    }
}
