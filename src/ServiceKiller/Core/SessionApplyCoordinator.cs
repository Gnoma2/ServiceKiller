using System;
using System.Collections.Generic;
using System.Linq;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class SessionApplyCoordinator
    {
        private readonly Logger _log;
        private readonly StateStore _persistentStore;
        private readonly StateStore _sessionStore;
        private readonly SessionRestoreManager _restoreManager;

        public SessionApplyCoordinator(Logger log)
        {
            _log = log;
            _persistentStore = new StateStore(log);
            _sessionStore = new StateStore(log, AppPaths.SessionState, "session");
            _restoreManager = new SessionRestoreManager(log);
        }

        public ApplyResult Apply(IEnumerable<TweakDefinition> source)
        {
            if (!PrivilegeHelper.IsAdministrator())
                throw new InvalidOperationException("El modo temporal necesita elevación para aplicar servicios/registro y programar su restauración.");

            List<TweakDefinition> selected = source == null ? new List<TweakDefinition>() : source.Where(delegate(TweakDefinition t) { return t != null && !t.IsProtectedInfo; }).ToList();
            ApplyResult combined = new ApplyResult();
            combined.SelectedActions = selected.Count;
            if (selected.Count == 0) return combined;

            ActiveState persistent = _persistentStore.Load();
            if (_persistentStore.SafetyLocked)
                throw new InvalidOperationException(_persistentStore.SafetyMessage);
            HashSet<string> persistentIds = new HashSet<string>(persistent.Tweaks.Select(delegate(TweakBackup b) { return b.TweakId; }), StringComparer.OrdinalIgnoreCase);

            ActiveState session = _sessionStore.Load();
            if (_sessionStore.SafetyLocked)
                throw new InvalidOperationException(_sessionStore.SafetyMessage);
            string originSid = PrivilegeHelper.CurrentUserSid();
            string originAccount = PrivilegeHelper.CurrentAccountName();
            if (!string.IsNullOrWhiteSpace(session.OriginUserSid) && !string.Equals(session.OriginUserSid, originSid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ya existe una sesión temporal pendiente perteneciente a otra cuenta de Windows. Restáurala antes de crear otra.");

            session.StatePurpose = "UntilRestart";
            session.OriginUserSid = originSid;
            session.OriginAccountName = originAccount;
            session.UserName = Environment.UserName;
            _sessionStore.Save(session); // write-ahead: existe antes de tocar el primer tweak

            bool taskPrepared = false;
            try
            {
                // La tarea queda registrada ANTES de modificar configuraciones persistentes.
                // Si Windows se reinicia inesperadamente a mitad del boost, el journal ya
                // está en disco y el próximo logon intentará restaurarlo.
                bool hasSessionPersistentCandidate = session.Tweaks.Count > 0 || selected.Any(delegate(TweakDefinition t)
                {
                    return t.ChangeKind != ChangeKind.Temporary && t.SupportsUntilRestartMode() && !persistentIds.Contains(t.Id);
                });
                if (hasSessionPersistentCandidate)
                {
                    // Re-registrar también protege una sesión temporal ya existente si la
                    // tarea se hubiese eliminado manualmente entre dos aplicaciones.
                    bool preserveExistingWorker = session.Tweaks.Count > 0;
                    _restoreManager.Prepare(originAccount, originSid, preserveExistingWorker);
                    taskPrepared = true;
                }

                List<TweakDefinition> executable = new List<TweakDefinition>();
                foreach (TweakDefinition tweak in selected)
                {
                    if (!tweak.SupportsUntilRestartMode())
                    {
                        combined.SkippedActions++;
                        combined.Messages.Add(tweak.Name + ": no compatible con modo temporal -> SIN ACCIÓN");
                        continue;
                    }
                    if (tweak.ChangeKind != ChangeKind.Temporary && persistentIds.Contains(tweak.Id))
                    {
                        combined.NoChangeActions++;
                        combined.Messages.Add(tweak.Name + ": ya está aplicado de forma PERSISTENTE; el reinicio no lo restaurará");
                        continue;
                    }
                    executable.Add(tweak);
                }

                TweakEngine sessionEngine = new TweakEngine(_log, _sessionStore);
                ApplyResult actual = sessionEngine.Apply(executable);
                combined.AppliedActions += actual.AppliedActions;
                combined.NoChangeActions += actual.NoChangeActions;
                combined.SkippedActions += actual.SkippedActions;
                combined.ErrorActions += actual.ErrorActions;
                combined.PersistentChanges += actual.PersistentChanges;
                combined.TemporaryActions += actual.TemporaryActions;
                combined.ProcessesClosed += actual.ProcessesClosed;
                combined.ServicesStopped += actual.ServicesStopped;
                combined.WindowsServicesStopped += actual.WindowsServicesStopped;
                combined.RestartRequired = false; // los tweaks que requieren reinicio se excluyen del modo sesión
                foreach (string message in actual.Messages) combined.Messages.Add(message);

                ActiveState after = _sessionStore.Load();
                if (_sessionStore.SafetyLocked)
                    throw new InvalidOperationException(_sessionStore.SafetyMessage);
                if (after.Tweaks.Count == 0)
                {
                    if (taskPrepared) _restoreManager.RemoveTask();
                    _sessionStore.ClearActive();
                }
                else
                {
                    combined.Messages.Add("AUTO-RESTAURACIÓN: programada para el próximo inicio de sesión tras reiniciar/cerrar sesión.");
                }
                return combined;
            }
            catch
            {
                // Si no llegó a existir ningún backup real, no dejamos una tarea vacía.
                try
                {
                    ActiveState afterFailure = _sessionStore.Load();
                    if (!_sessionStore.SafetyLocked && afterFailure.Tweaks.Count == 0)
                    {
                        if (taskPrepared) _restoreManager.RemoveTask();
                        _sessionStore.ClearActive();
                    }
                    else if (_sessionStore.SafetyLocked)
                    {
                        _log.Warn("El journal temporal entró en bloqueo de seguridad durante un fallo; no se elimina ni la tarea ni el journal.");
                    }
                }
                catch { }
                throw;
            }
        }

        public List<string> RestoreNow()
        {
            StateStore sessionStore = new StateStore(_log, AppPaths.SessionState, "session");
            ActiveState state = sessionStore.Load();
            if (sessionStore.SafetyLocked)
                throw new InvalidOperationException(sessionStore.SafetyMessage);
            List<string> ids = state.Tweaks.Select(delegate(TweakBackup b) { return b.TweakId; }).ToList();
            if (ids.Count == 0)
            {
                bool removed = _restoreManager.RemoveTask();
                sessionStore.ClearActive();
                return new List<string> { removed ? "No había cambios temporales pendientes." : "No había cambios temporales pendientes, pero la tarea automática no pudo eliminarse y debe revisarse." };
            }

            string originSid = !string.IsNullOrWhiteSpace(state.OriginUserSid) ? state.OriginUserSid : PrivilegeHelper.CurrentUserSid();
            TweakEngine engine = new TweakEngine(_log, sessionStore);
            List<string> messages = engine.Restore(ids);
            if (engine.GetActiveBackups().Count == 0 && !_restoreManager.RemoveTask())
                messages.Add("Tarea de restauración automática: ERROR - Windows no confirmó su eliminación.");
            RestorationVerifier.BuildAndSave(state, originSid, messages, "MANUAL");
            _log.Info("Informe de verificación de restauración manual guardado en " + AppPaths.LastSessionRestoreReport);
            return messages;
        }
    }
}
