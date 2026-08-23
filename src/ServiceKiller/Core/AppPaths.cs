using System;
using System.IO;

namespace ServiceKillerV1.Core
{
    public static class AppPaths
    {
        // Datos de sistema / journal: compartidos entre V1.0, V1.01, V1.02 y V1.03.
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ServiceKiller");
        public static readonly string Backups = Path.Combine(Root, "Backups");
        public static readonly string Logs = Path.Combine(Root, "Logs");
        public static readonly string ActiveState = Path.Combine(Root, "active-state.json");
        // Journal separado para el modo "TEMPORAL HASTA REINICIO". Nunca se mezcla
        // con active-state.json para no restaurar por accidente tweaks persistentes.
        public static readonly string SessionState = Path.Combine(Root, "session-state.json");
        public static readonly string SessionRestoreRoot = Path.Combine(Root, "SessionRestore");
        // Restaurador temporal protegido: la tarea no apunta al EXE ubicado por el usuario
        // (Descargas/Escritorio/etc.), sino a una copia de la misma build dentro del área
        // de máquina protegida. Así la restauración pendiente no depende de un archivo
        // modificable por procesos no elevados.
        public static readonly string SessionRestoreExecutable = Path.Combine(SessionRestoreRoot, "ServiceKiller.SessionRestore.exe");
        public static readonly string SessionRestoreConfig = SessionRestoreExecutable + ".config";
        public static readonly string SessionRestoreHash = Path.Combine(SessionRestoreRoot, "restore-worker.sha256");
        public static readonly string SessionRestoreSourcePath = Path.Combine(SessionRestoreRoot, "restore-source.txt");
        public static readonly string SessionTaskXml = Path.Combine(SessionRestoreRoot, "session-restore-task.xml");
        public static readonly string SessionRestoreCleanupReady = Path.Combine(SessionRestoreRoot, "cleanup-ready.txt");
        public const string SessionTaskName = "ServiceKiller - Restaurar sesion temporal";
        public static readonly string LogFile = Path.Combine(Logs, "ServiceKiller.log");
        public static readonly string LastSessionRestoreReport = Path.Combine(Logs, "last-session-restore-verification.txt");
        public static readonly string LegacyUiState = Path.Combine(Root, "ui-state.txt");

        // Datos puramente de interfaz/sesión: deben ser escribibles sin administrador.
        public static readonly string UserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ServiceKiller");
        public static readonly string UserLogs = Path.Combine(UserRoot, "Logs");
        public static readonly string UserLogFile = Path.Combine(UserLogs, "ServiceKiller-session.log");
        public static readonly string UiState = Path.Combine(UserRoot, "ui-state.txt");
        public static readonly string CustomApps = Path.Combine(UserRoot, "custom-apps.json");
        public static readonly string CustomAppsBackup = Path.Combine(UserRoot, "custom-apps.json.bak");
        public static readonly string Profiles = Path.Combine(UserRoot, "profiles.json");
        public static readonly string ProfilesBackup = Path.Combine(UserRoot, "profiles.json.bak");
        public static readonly string LastBoostSummary = Path.Combine(UserRoot, "last-boost.txt");

        public static void EnsureMachine()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(SessionRestoreRoot);

            // Los journals y el restaurador automático se consumen posteriormente con
            // privilegios elevados. El árbol de máquina debe ser legible pero no modificable
            // por usuarios/procesos no elevados.
            MachineDataSecurity.ProtectMachineTree(Root, Backups, Logs, SessionRestoreRoot);
        }

        public static void EnsureUser()
        {
            Directory.CreateDirectory(UserRoot);
            Directory.CreateDirectory(UserLogs);
        }

        // Compatibilidad fuente con V1/V1.01: operaciones que llaman Ensure() son de escritura de máquina.
        public static void Ensure()
        {
            EnsureMachine();
        }
    }
}
