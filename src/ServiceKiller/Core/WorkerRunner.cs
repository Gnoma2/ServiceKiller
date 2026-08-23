using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ServiceKillerV1.Data;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public static class WorkerRunner
    {
        public static bool IsWorkerRequest(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], "--worker", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static int Run(string[] args)
        {
            string operation = GetArg(args, "--worker");
            string idsText = GetArg(args, "--ids");
            string resultPath = GetArg(args, "--result");
            string originSid = GetArg(args, "--origin-sid");
            string workerSha256 = GetArg(args, "--worker-sha256");

            // El auto-restaurador se lanza desde Task Scheduler con la cuenta de origen y nivel Highest.
            // Conservamos el tratamiento especial porque se inicia fuera de la GUI.
            if (string.Equals(operation, "restore-session-auto", StringComparison.OrdinalIgnoreCase))
                return RunAutomaticSessionRestore(originSid, resultPath, workerSha256);

            if (!PrivilegeHelper.IsAdministrator())
            {
                WriteResult(resultPath, false, false, "El worker no recibió permisos de administrador.");
                return 5;
            }

            // Evita tocar HKCU de otra cuenta cuando Windows solicita credenciales de un administrador distinto.
            string currentSid = PrivilegeHelper.CurrentUserSid();
            if (!string.IsNullOrEmpty(originSid) && !string.Equals(originSid, currentSid, StringComparison.OrdinalIgnoreCase))
            {
                WriteResult(resultPath, false, false,
                    "La elevación se realizó con una cuenta de Windows distinta. ServiceKiller ha cancelado la operación para no modificar el HKCU/inicio automático de otro usuario. Inicia sesión con una cuenta administradora o eleva con la misma cuenta.");
                return 6;
            }

            Logger log = new Logger();
            MachineOperationLock operationLock = null;

            try
            {
                operationLock = MachineOperationLock.Acquire(30000);
                // El journal temporal es independiente de active-state.json. Una avería
                // del journal persistente no debe impedir recuperar una sesión temporal.
                if (string.Equals(operation, "restore-all", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> messages = new List<string>();

                    StateStore sessionStore = new StateStore(log, AppPaths.SessionState, "session");
                    ActiveState sessionState = sessionStore.Load();
                    if (sessionStore.SafetyLocked)
                    {
                        WriteResult(resultPath, false, false, sessionStore.SafetyMessage);
                        return 17;
                    }
                    if (sessionState.Tweaks.Count > 0)
                    {
                        messages.Add("=== SESIÓN TEMPORAL ===");
                        SessionApplyCoordinator coordinator = new SessionApplyCoordinator(log);
                        messages.AddRange(coordinator.RestoreNow());
                    }

                    StateStore persistentStore = new StateStore(log);
                    ActiveState persistentState = persistentStore.Load();
                    if (persistentStore.SafetyLocked)
                    {
                        WriteResult(resultPath, false, false, persistentStore.SafetyMessage);
                        return 18;
                    }
                    if (persistentState.Tweaks.Count > 0)
                    {
                        if (messages.Count > 0) messages.Add(string.Empty);
                        messages.Add("=== CAMBIOS PERSISTENTES ===");
                        TweakEngine persistentEngine = new TweakEngine(log, persistentStore);
                        List<string> allIds = persistentState.Tweaks.Select(delegate(TweakBackup b) { return b.TweakId; }).ToList();
                        messages.AddRange(persistentEngine.Restore(allIds));
                    }

                    if (messages.Count == 0) messages.Add("No había cambios pendientes de ServiceKiller.");
                    bool anyError = HasErrors(messages);
                    WriteResult(resultPath, !anyError, false, string.Join(Environment.NewLine, messages.ToArray()));
                    return anyError ? 19 : 0;
                }

                if (string.Equals(operation, "restore-session-now", StringComparison.OrdinalIgnoreCase))
                {
                    SessionApplyCoordinator coordinator = new SessionApplyCoordinator(log);
                    List<string> messages = coordinator.RestoreNow();
                    bool anyError = messages.Any(delegate(string m) { return m.IndexOf(": ERROR -", StringComparison.OrdinalIgnoreCase) >= 0; });
                    WriteResult(resultPath, !anyError, false, string.Join(Environment.NewLine, messages.ToArray()));
                    return anyError ? 15 : 0;
                }

                StateStore store = new StateStore(log);
                TweakEngine engine = new TweakEngine(log, store);
                store.Load();
                if (store.SafetyLocked)
                {
                    WriteResult(resultPath, false, false, store.SafetyMessage);
                    return 7;
                }

                List<string> ids = ParseIds(idsText);
                if (ids.Count == 0)
                {
                    WriteResult(resultPath, false, false, "No se recibió ninguna acción válida.");
                    return 8;
                }

                List<TweakDefinition> allTweaks = TweakCatalog.Create();
                CustomAppStore customStore = new CustomAppStore(log);
                foreach (CustomApplicationInfo app in customStore.Load())
                {
                    allTweaks.Add(CustomAppStore.ToTweak(app));
                    allTweaks.Add(CustomAppStore.ToStartupTweak(app));
                }
                Dictionary<string, TweakDefinition> catalog = allTweaks.ToDictionary(delegate(TweakDefinition t) { return t.Id; }, StringComparer.OrdinalIgnoreCase);
                List<TweakDefinition> tweaks = new List<TweakDefinition>();
                foreach (string id in ids)
                {
                    TweakDefinition tweak;
                    if (!catalog.TryGetValue(id, out tweak) || tweak.IsProtectedInfo)
                    {
                        WriteResult(resultPath, false, false, "ID de tweak no válido o protegido: " + id);
                        return 9;
                    }
                    tweaks.Add(tweak);
                }

                if (string.Equals(operation, "apply", StringComparison.OrdinalIgnoreCase))
                {
                    Stopwatch boostTimer = Stopwatch.StartNew();
                    ApplyResult applied = engine.Apply(tweaks);
                    boostTimer.Stop();
                    applied.DurationMilliseconds = boostTimer.ElapsedMilliseconds;
                    bool anyError = applied.ErrorActions > 0 || HasErrors(applied.Messages);
                    string text = "Cambios persistentes nuevos: " + applied.PersistentChanges + Environment.NewLine +
                                  "Acciones temporales ejecutadas: " + applied.TemporaryActions + Environment.NewLine +
                                  "Procesos cerrados: " + applied.ProcessesClosed + Environment.NewLine +
                                  "Servicios Windows detenidos: " + applied.WindowsServicesStopped + Environment.NewLine +
                                  "Servicios residentes de apps detenidos: " + applied.ServicesStopped + Environment.NewLine + Environment.NewLine +
                                  string.Join(Environment.NewLine, applied.Messages.ToArray());
                    WriteApplyResult(resultPath, !anyError, applied.RestartRequired, applied, text);
                    return anyError ? 10 : 0;
                }

                if (string.Equals(operation, "apply-session", StringComparison.OrdinalIgnoreCase))
                {
                    SessionApplyCoordinator coordinator = new SessionApplyCoordinator(log);
                    Stopwatch boostTimer = Stopwatch.StartNew();
                    ApplyResult applied = coordinator.Apply(tweaks);
                    boostTimer.Stop();
                    applied.DurationMilliseconds = boostTimer.ElapsedMilliseconds;
                    bool anyError = applied.ErrorActions > 0 || HasErrors(applied.Messages);
                    string text = "Cambios de sesión con journal: " + applied.PersistentChanges + Environment.NewLine +
                                  "Acciones temporales ejecutadas: " + applied.TemporaryActions + Environment.NewLine +
                                  "Procesos cerrados: " + applied.ProcessesClosed + Environment.NewLine +
                                  "Servicios Windows detenidos: " + applied.WindowsServicesStopped + Environment.NewLine +
                                  "Servicios residentes de apps detenidos: " + applied.ServicesStopped + Environment.NewLine +
                                  "Restauración automática: " + (new StateStore(log, AppPaths.SessionState, "session").ExistsOnDisk() ? "PROGRAMADA / PENDIENTE" : "NO NECESARIA") + Environment.NewLine + Environment.NewLine +
                                  string.Join(Environment.NewLine, applied.Messages.ToArray());
                    WriteApplyResult(resultPath, !anyError, false, applied, text);
                    return anyError ? 16 : 0;
                }

                if (string.Equals(operation, "restore", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> messages = engine.Restore(ids);
                    bool anyError = HasErrors(messages);
                    WriteResult(resultPath, !anyError, false, string.Join(Environment.NewLine, messages.ToArray()));
                    return anyError ? 11 : 0;
                }

                WriteResult(resultPath, false, false, "Operación elevada desconocida: " + operation);
                return 12;
            }
            catch (Exception ex)
            {
                log.Error("Worker elevado: " + ex.Message);
                WriteResult(resultPath, false, false, "La operación elevada se interrumpió: " + ex.Message + Environment.NewLine + "Revisa LOG y RESTAURAR antes de continuar.");
                return 13;
            }
            finally
            {
                if (operationLock != null) operationLock.Dispose();
            }
        }

        private static int RunAutomaticSessionRestore(string originSidArg, string resultPath, string expectedWorkerSha256)
        {
            Logger log = new Logger();
            SessionRestoreManager taskManager = new SessionRestoreManager(log);
            MachineOperationLock operationLock = null;
            try
            {
                if (!PrivilegeHelper.IsAdministrator())
                {
                    WriteResult(resultPath, false, false, "El restaurador automático no tiene privilegios suficientes.");
                    return 20;
                }

                if (!SessionRestoreManager.VerifyCurrentWorkerIntegrity(expectedWorkerSha256))
                {
                    log.Error("El restaurador automático no coincide con la copia protegida/huella SHA-256 esperada. La tarea y el journal se conservan.");
                    WriteResult(resultPath, false, false, "Verificación de integridad del restaurador automático fallida. No se ha ejecutado ninguna restauración.");
                    return 26;
                }

                operationLock = MachineOperationLock.Acquire(30000);

                StateStore sessionStore = new StateStore(log, AppPaths.SessionState, "session");
                ActiveState state = sessionStore.Load();
                if (sessionStore.SafetyLocked)
                {
                    log.Error(sessionStore.SafetyMessage);
                    WriteResult(resultPath, false, false, sessionStore.SafetyMessage);
                    return 21;
                }

                if (state.Tweaks.Count == 0)
                {
                    bool removed = taskManager.RemoveTask();
                    sessionStore.ClearActive();
                    WriteResult(resultPath, removed, false, removed ? "No había cambios de sesión pendientes." : "No había cambios de sesión pendientes, pero Windows no confirmó la eliminación de la tarea automática.");
                    return removed ? 0 : 27;
                }

                string originSid = !string.IsNullOrWhiteSpace(state.OriginUserSid) ? state.OriginUserSid : originSidArg;
                if (string.IsNullOrWhiteSpace(originSid))
                {
                    log.Error("El journal temporal no contiene SID de origen. La tarea se conserva para diagnóstico.");
                    WriteResult(resultPath, false, false, "Falta SID de origen en session-state.json.");
                    return 22;
                }

                bool needsUserHive = state.Tweaks.Any(delegate(TweakBackup b)
                {
                    return b.RegistryValues.Any(delegate(RegistryValueBackup r) { return string.Equals(r.Hive, "HKCU", StringComparison.OrdinalIgnoreCase); }) ||
                           b.StartupEntries.Any(delegate(StartupEntryBackup r) { return string.Equals(r.Hive, "HKCU", StringComparison.OrdinalIgnoreCase); });
                });
                if (needsUserHive && !SessionRestoreManager.IsUserHiveLoaded(originSid))
                {
                    log.Warn("El hive HKEY_USERS\\" + originSid + " aún no está cargado. Se conserva la tarea para reintentar en el próximo logon.");
                    WriteResult(resultPath, false, false, "El hive de usuario todavía no estaba disponible; se reintentará automáticamente.");
                    return 23;
                }

                TweakEngine engine = new TweakEngine(log, sessionStore, originSid);
                List<string> ids = state.Tweaks.Select(delegate(TweakBackup b) { return b.TweakId; }).ToList();
                List<string> messages = engine.Restore(ids);
                bool anyError = HasErrors(messages);
                if (!anyError && engine.GetActiveBackups().Count == 0)
                {
                    if (!taskManager.RemoveTask())
                    {
                        anyError = true;
                        messages.Add("Tarea de restauración automática: ERROR - Windows no confirmó su eliminación.");
                        log.Warn("La restauración de componentes terminó, pero la tarea automática no quedó eliminada.");
                    }
                    else
                    {
                        log.Info("Restauración automática de la sesión temporal completada.");
                        if (!taskManager.MarkProtectedWorkerForDeferredCleanup(originSid))
                            messages.Add("Restaurador protegido: restauración completada, pero la limpieza diferida no pudo prepararse.");
                    }
                }
                else
                {
                    log.Warn("La restauración automática quedó incompleta; la tarea se conserva para reintentar.");
                }

                string verification = RestorationVerifier.BuildAndSave(state, originSid, messages, "AUTOMÁTICA / LOGON");
                log.Info("Informe de verificación de restauración guardado en " + AppPaths.LastSessionRestoreReport);
                WriteResult(resultPath, !anyError, false, string.Join(Environment.NewLine, messages.ToArray()) + Environment.NewLine + Environment.NewLine + verification);
                return anyError ? 24 : 0;
            }
            catch (Exception ex)
            {
                log.Error("Auto-restauración de sesión: " + ex.Message);
                WriteResult(resultPath, false, false, "Auto-restauración interrumpida: " + ex.Message);
                return 25;
            }
            finally
            {
                if (operationLock != null) operationLock.Dispose();
            }
        }

        private static bool HasErrors(IEnumerable<string> messages)
        {
            return messages != null && messages.Any(delegate(string m) { return m.IndexOf(": ERROR -", StringComparison.OrdinalIgnoreCase) >= 0; });
        }

        private static List<string> ParseIds(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            string[] parts = text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = part.Trim();
                if (id.Length > 0 && !result.Contains(id, StringComparer.OrdinalIgnoreCase)) result.Add(id);
            }
            return result;
        }

        private static string GetArg(string[] args, string name)
        {
            if (args == null) return string.Empty;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1] ?? string.Empty;
            return string.Empty;
        }


        private static void WriteApplyResult(string path, bool ok, bool restart, ApplyResult result, string message)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
                string header = (ok ? "OK" : "ERROR") + "|" + (restart ? "1" : "0") +
                                "|" + result.SelectedActions +
                                "|" + result.AppliedActions +
                                "|" + result.NoChangeActions +
                                "|" + result.SkippedActions +
                                "|" + result.ErrorActions +
                                "|" + result.PersistentChanges +
                                "|" + result.TemporaryActions +
                                "|" + result.ProcessesClosed +
                                "|" + result.ServicesStopped +
                                "|" + result.DurationMilliseconds +
                                "|" + result.WindowsServicesStopped;
                System.IO.File.WriteAllText(path, header + Environment.NewLine + (message ?? string.Empty), Encoding.UTF8);
            }
            catch { }
        }
        private static void WriteResult(string path, bool ok, bool restart, string message)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
                System.IO.File.WriteAllText(path, (ok ? "OK" : "ERROR") + "|" + (restart ? "1" : "0") + Environment.NewLine + (message ?? string.Empty), Encoding.UTF8);
            }
            catch { }
        }
    }
}
