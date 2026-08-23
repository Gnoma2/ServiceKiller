using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ServiceKillerV1.Data;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class DiagnosticReportBuilder
    {
        private readonly Logger _log;

        public DiagnosticReportBuilder(Logger log)
        {
            _log = log;
        }

        public string Build(string version, bool isAdministrator, ApplyMode applyMode)
        {
            StringBuilder sb = new StringBuilder();
            StateStore persistentStore = new StateStore(_log);
            StateStore sessionStore = new StateStore(_log, AppPaths.SessionState, "session");
            ActiveState persistent = persistentStore.Load();
            ActiveState session = sessionStore.Load();
            HashSet<string> persistentIds = new HashSet<string>(persistent.Tweaks.Select(delegate(TweakBackup b) { return b.TweakId; }), StringComparer.OrdinalIgnoreCase);
            HashSet<string> sessionIds = new HashSet<string>(session.Tweaks.Select(delegate(TweakBackup b) { return b.TweakId; }), StringComparer.OrdinalIgnoreCase);
            HashSet<string> combined = new HashSet<string>(persistentIds, StringComparer.OrdinalIgnoreCase);
            combined.UnionWith(sessionIds);

            TweakEngine engine = new TweakEngine(_log, persistentStore);
            SystemMetrics metrics = new SystemMetricsReader(engine.Services).Read();

            List<TweakDefinition> catalog = TweakCatalog.Create();
            try
            {
                CustomAppStore customStore = new CustomAppStore(_log);
                foreach (CustomApplicationInfo app in customStore.Load())
                {
                    catalog.Add(CustomAppStore.ToTweak(app));
                    catalog.Add(CustomAppStore.ToStartupTweak(app));
                }
            }
            catch { }

            sb.AppendLine("============= SERVICEKILLER - DIAGNÓSTICO ANONIMIZADO =============");
            sb.AppendLine("Generado: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Versión: " + version);
            sb.AppendLine("Equipo: <EQUIPO>");
            sb.AppendLine("Usuario: <USUARIO>");
            sb.AppendLine("GUI: " + (isAdministrator ? "ADMINISTRADOR" : "SOLO LECTURA"));
            sb.AppendLine("Modo seleccionado: " + (applyMode == ApplyMode.UntilRestart ? "TEMPORAL HASTA REINICIO" : "PERSISTENTE"));
            sb.AppendLine("SO: " + WindowsCompatibility.FriendlyName + (Environment.Is64BitOperatingSystem ? " · 64 bits" : " · 32 bits"));
            sb.AppendLine("Compatibilidad: " + WindowsCompatibility.CompatibilitySummary);
            sb.AppendLine(".NET: " + Environment.Version);
            sb.AppendLine();

            sb.AppendLine("MÉTRICAS ACTUALES");
            sb.AppendLine("Servicios ejecutándose: " + metrics.RunningServices);
            sb.AppendLine("Procesos: " + metrics.Processes);
            sb.AppendLine("RAM total: " + FormatMb(metrics.TotalMemoryMb));
            sb.AppendLine("RAM usada: " + FormatMb(metrics.UsedMemoryMb));
            sb.AppendLine("RAM disponible: " + FormatMb(metrics.AvailableMemoryMb));
            sb.AppendLine();

            sb.AppendLine("JOURNALS / RESTAURACIÓN");
            int totalPending = persistent.Tweaks.Count + session.Tweaks.Count;
            sb.AppendLine("Pendientes totales de ServiceKiller: " + totalPending + " · persistentes: " + persistent.Tweaks.Count + " · sesión temporal: " + session.Tweaks.Count);
            sb.AppendLine("Journal persistente: " + (persistentStore.ExistsOnDisk() ? "PRESENTE" : "AUSENTE") + " · " + persistent.Tweaks.Count + " tweak(s)");
            foreach (TweakBackup b in persistent.Tweaks) sb.AppendLine("  PERSISTENTE: " + b.TweakId + " · " + b.TweakName);
            if (persistent.Tweaks.Count > 0)
                sb.AppendLine("  ATENCIÓN: estos cambios siguen activos hasta usar RESTAURAR TODO PENDIENTE o restaurarlos individualmente.");
            string sessionJournalStatus = session.Tweaks.Count > 0 ? "PRESENTE / PENDIENTE" : (sessionStore.ExistsOnDisk() ? "PRESENTE PERO VACÍO" : "AUSENTE");
            sb.AppendLine("Journal temporal: " + sessionJournalStatus + " · " + session.Tweaks.Count + " tweak(s)");
            foreach (TweakBackup b in session.Tweaks) sb.AppendLine("  SESIÓN: " + b.TweakId + " · " + b.TweakName);
            bool sessionPending = session.Tweaks.Count > 0;
            sb.AppendLine("Tarea de restauración temporal: " + QueryTaskStatus(sessionPending));
            sb.AppendLine("Restaurador temporal protegido: " + SessionRestoreManager.GetProtectedWorkerStatus());
            sb.AppendLine();

            sb.AppendLine("ÚLTIMO INFORME DE RESTAURACIÓN TEMPORAL (HISTÓRICO)");
            sb.AppendLine("Nota: este bloque corresponde únicamente a la última restauración del modo TEMPORAL; no describe una restauración persistente realizada después.");
            if (File.Exists(AppPaths.LastSessionRestoreReport))
            {
                try
                {
                    sb.AppendLine("Archivo actualizado: " + File.GetLastWriteTime(AppPaths.LastSessionRestoreReport).ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine(File.ReadAllText(AppPaths.LastSessionRestoreReport));
                }
                catch (Exception ex) { sb.AppendLine("No se pudo leer: " + ex.Message); }
            }
            else sb.AppendLine("No existe todavía un informe detallado de restauración temporal.");
            sb.AppendLine();

            sb.AppendLine("ÚLTIMO BOOST");
            if (File.Exists(AppPaths.LastBoostSummary))
            {
                try { sb.AppendLine(File.ReadAllText(AppPaths.LastBoostSummary)); }
                catch (Exception ex) { sb.AppendLine("No se pudo leer: " + ex.Message); }
            }
            else sb.AppendLine("No disponible.");
            sb.AppendLine();

            sb.AppendLine("ESTADO ACTUAL DE FUNCIONES WINDOWS");
            foreach (TweakDefinition tweak in catalog.Where(delegate(TweakDefinition t) { return !t.IsApplication && !t.IsProtectedInfo; }))
            {
                try
                {
                    TweakRuntimeState state = engine.GetRuntimeState(tweak, combined);
                    state.IsSessionApplied = sessionIds.Contains(tweak.Id);
                    string sk = sessionIds.Contains(tweak.Id) ? "SESIÓN" : (persistentIds.Contains(tweak.Id) ? "PERSISTENTE" : "NO");
                    sb.AppendLine("- " + tweak.Name + " [" + tweak.Id + "] | Estado: " + state.Summary + " | SK: " + sk);
                    if (!string.IsNullOrWhiteSpace(state.Details))
                    {
                        string[] lines = state.Details.Replace("\r", "").Split('\n');
                        foreach (string line in lines) if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine("    " + line.Trim());
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("- " + tweak.Name + " [" + tweak.Id + "] | ERROR DE LECTURA: " + ex.Message);
                }
            }
            sb.AppendLine();

            sb.AppendLine("ESTADO ACTUAL DE APLICACIONES");
            foreach (TweakDefinition tweak in catalog.Where(delegate(TweakDefinition t) { return t.IsApplication && t.ChangeKind == ChangeKind.Temporary; }))
            {
                try
                {
                    TweakRuntimeState state = engine.GetRuntimeState(tweak, combined);
                    sb.AppendLine("- " + tweak.Name + " | " + state.Summary +
                                  (state.ApplicationProcessCount > 0 ? " | " + state.ApplicationProcessCount + " proc | " + FormatMb(state.ApplicationMemoryMb) + " RAM" : ""));
                }
                catch (Exception ex) { sb.AppendLine("- " + tweak.Name + " | ERROR: " + ex.Message); }
            }
            sb.AppendLine();

            sb.AppendLine("INICIO AUTOMÁTICO DE APLICACIONES");
            foreach (TweakDefinition tweak in catalog.Where(delegate(TweakDefinition t) { return t.IsApplication && (t.IsStartupOnlyAction || t.IsCustomStartupAction); }))
            {
                try
                {
                    TweakRuntimeState state = engine.GetRuntimeState(tweak, combined);
                    string sk = sessionIds.Contains(tweak.Id) ? "SESIÓN" : (persistentIds.Contains(tweak.Id) ? "PERSISTENTE" : "NO");
                    sb.AppendLine("- " + tweak.Name + " [" + tweak.Id + "] | " + state.Summary + " | SK: " + sk);
                    if (!string.IsNullOrWhiteSpace(state.Details))
                    {
                        string[] lines = state.Details.Replace("\r", "").Split('\n');
                        foreach (string line in lines) if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine("    " + line.Trim());
                    }
                }
                catch (Exception ex) { sb.AppendLine("- " + tweak.Name + " | ERROR: " + ex.Message); }
            }
            sb.AppendLine();

            sb.AppendLine("ÚLTIMAS LÍNEAS DEL LOG DE MÁQUINA");
            sb.AppendLine(Tail(AppPaths.LogFile, 180));
            sb.AppendLine();
            sb.AppendLine("ÚLTIMAS LÍNEAS DEL LOG DE USUARIO");
            sb.AppendLine(Tail(AppPaths.UserLogFile, 100));
            sb.AppendLine();

            sb.AppendLine("RUTAS");
            sb.AppendLine("Journal persistente: " + AppPaths.ActiveState);
            sb.AppendLine("Journal temporal: " + AppPaths.SessionState);
            sb.AppendLine("Informe última restauración: " + AppPaths.LastSessionRestoreReport);
            sb.AppendLine("Log máquina: " + AppPaths.LogFile);
            sb.AppendLine("Log usuario: " + AppPaths.UserLogFile);
            sb.AppendLine("=======================================================================");
            sb.AppendLine("PRIVACIDAD: anonimización automática/best effort. Revisa el contenido antes de publicarlo en un Issue o foro.");
            return Anonymize(sb.ToString(), persistent, session);
        }

        private static string Anonymize(string text, params ActiveState[] states)
        {
            string result = text ?? string.Empty;

            // Rutas conocidas de la cuenta interactiva. Se sustituyen antes de aplicar
            // expresiones genéricas para conservar información técnica útil del resto de la ruta.
            result = ReplaceSensitive(result, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%");
            result = ReplaceSensitive(result, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%");
            result = ReplaceSensitive(result, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
            result = ReplaceSensitive(result, Environment.MachineName, "<EQUIPO>");
            result = ReplaceSensitive(result, Environment.UserName, "<USUARIO>");

            try { result = ReplaceSensitive(result, PrivilegeHelper.CurrentAccountName(), "<CUENTA_WINDOWS>"); } catch { }
            try { result = ReplaceSensitive(result, PrivilegeHelper.CurrentUserSid(), "<SID_USUARIO>"); } catch { }

            if (states != null)
            {
                foreach (ActiveState state in states)
                {
                    if (state == null) continue;
                    result = ReplaceSensitive(result, state.MachineName, "<EQUIPO>");
                    result = ReplaceSensitive(result, state.UserName, "<USUARIO>");
                    result = ReplaceSensitive(result, state.OriginAccountName, "<CUENTA_WINDOWS>");
                    result = ReplaceSensitive(result, state.OriginUserSid, "<SID_USUARIO>");
                }
            }

            // Defensa adicional para contenido histórico de logs/informes y rutas de aplicaciones.
            result = Regex.Replace(result, @"(?i)\b[A-Z]:\\Users\\[^\\\r\n]+", "%USERPROFILE%");
            result = Regex.Replace(result, @"(?i)S-1-5-21-(?:\d+-){3}\d+", "<SID_USUARIO>");
            result = Regex.Replace(result, @"(?i)\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", "<EMAIL>");
            return result;
        }

        private static string ReplaceSensitive(string text, string value, string replacement)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(value)) return text ?? string.Empty;
            return Regex.Replace(text, Regex.Escape(value), replacement, RegexOptions.IgnoreCase);
        }

        private static string QueryTaskStatus(bool sessionPending)
        {
            // Si no hay sesión temporal, la tarea no forma parte del estado esperado y
            // no merece abrir Task Scheduler COM solo para confirmar una ausencia.
            if (!sessionPending) return "NO APLICA · no existe sesión temporal activa";

            try
            {
                bool exists = TaskSchedulerInterop.TaskExists(AppPaths.SessionTaskName);
                if (exists) return "PRESENTE · sesión temporal pendiente";
                return "AUSENTE · ATENCIÓN: hay journal temporal pero no existe tarea de auto-restauración";
            }
            catch (Exception ex) { return "NO VERIFICABLE · hay sesión temporal pendiente: " + ex.Message; }
        }

        private static string Tail(string path, int maxLines)
        {
            try
            {
                if (!File.Exists(path)) return "(archivo no existente)";
                string[] lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - maxLines);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex) { return "(no accesible: " + ex.Message + ")"; }
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string FormatMb(long mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0") + " GB";
            return mb + " MB";
        }
    }
}
