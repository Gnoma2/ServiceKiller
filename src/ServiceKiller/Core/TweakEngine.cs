using System;
using System.Collections.Generic;
using System.Linq;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class ApplyResult
    {
        public ApplyResult()
        {
            Messages = new List<string>();
        }

        public int SelectedActions { get; set; }
        public int AppliedActions { get; set; }
        public int NoChangeActions { get; set; }
        public int SkippedActions { get; set; }
        public int ErrorActions { get; set; }
        public int PersistentChanges { get; set; }
        public int TemporaryActions { get; set; }
        public int ProcessesClosed { get; set; }
        // Servicios residentes de aplicaciones detenidos por ProcessManager (p.ej. reWASDService).
        public int ServicesStopped { get; set; }
        // Servicios Windows de los tweaks que estaban Running y quedaron realmente Stopped.
        public int WindowsServicesStopped { get; set; }
        public long DurationMilliseconds { get; set; }
        public bool RestartRequired { get; set; }
        public List<string> Messages { get; private set; }
    }

    public sealed class TweakEngine
    {
        private readonly Logger _log;
        private readonly StateStore _store;
        private readonly WindowsServiceManager _services;
        private readonly RegistryManager _registry;
        private readonly BootManager _boot;
        private readonly StartupManager _startup;
        private readonly ProcessManager _processes;
        private readonly ApplicationDetector _applications;

        public TweakEngine(Logger log, StateStore store)
            : this(log, store, null)
        {
        }

        public TweakEngine(Logger log, StateStore store, string userSidOverride)
        {
            _log = log;
            _store = store;
            _services = new WindowsServiceManager(log);
            _registry = new RegistryManager(log, userSidOverride);
            _boot = new BootManager(log);
            _startup = new StartupManager(log, userSidOverride);
            _processes = new ProcessManager(log, _services);
            _applications = new ApplicationDetector(_processes, log);
        }

        public WindowsServiceManager Services { get { return _services; } }
        public ProcessManager Processes { get { return _processes; } }

        public ApplyResult Apply(IEnumerable<TweakDefinition> tweaks)
        {
            // V1.1.2.7: antes de añadir nuevos cambios, sanea entradas conocidas que
            // puedan haber quedado en el journal por un rollback incompleto de versiones
            // anteriores. Solo escribe si este proceso está realmente elevado.
            if (PrivilegeHelper.IsAdministrator()) RepairKnownStaleJournalEntries();

            ApplyResult result = new ApplyResult();
            List<TweakDefinition> tweakList = tweaks == null
                ? new List<TweakDefinition>()
                : tweaks.Where(delegate(TweakDefinition t) { return t != null && !t.IsProtectedInfo; }).ToList();
            result.SelectedActions = tweakList.Count;

            ActiveState state = _store.Load();
            _store.ArchiveActive("before-apply");

            foreach (TweakDefinition tweak in tweakList)
            {
                if (tweak.IsApplication)
                {
                    ApplicationPresenceResult presence = _applications.Detect(tweak);
                    if (presence.State == ApplicationInstallState.NotInstalled)
                    {
                        _log.Info("Aplicación no instalada, se omite: " + tweak.Name);
                        result.SkippedActions++;
                        result.Messages.Add(tweak.Name + ": no instalada -> SIN ACCIÓN");
                        continue;
                    }
                }

                try
                {
                    if (tweak.ChangeKind == ChangeKind.Temporary)
                    {
                        ProcessCloseResult close = _processes.CloseDetailedForTweak(tweak);
                        result.ProcessesClosed += close.ClosedProcessCount;
                        result.ServicesStopped += close.ServicesStopped;

                        bool changed = close.ClosedProcessCount > 0 || close.ServicesStopped > 0;
                        if (changed)
                        {
                            result.TemporaryActions++;
                            result.AppliedActions++;
                        }
                        else
                        {
                            result.NoChangeActions++;
                        }

                        string resources = close.InitialProcessCount > 0
                            ? " · procesos " + close.InitialProcessCount + " -> " + close.RemainingProcessCount + ", RAM " + close.MemoryBeforeMb + " -> " + close.MemoryAfterMb + " MB"
                            : string.Empty;
                        result.Messages.Add(tweak.Name + ": " +
                                            (changed ? "cierre temporal ejecutado" : "sin proceso/servicio activo -> SIN CAMBIO") +
                                            (close.ClosedProcessCount > 0 ? " · cerrados: " + close.ClosedProcessCount + " proceso(s)" : "") +
                                            (close.ServicesStopped > 0 ? " · servicios detenidos: " + close.ServicesStopped : "") +
                                            resources +
                                            (close.Remains ? " · queda residencia activa; revisar LOG" : ""));

                        if (close.Remains)
                            _log.Warn("Acción temporal parcial: sigue detectándose residencia para " + tweak.Id);
                        else if (changed)
                            _log.Info("Acción temporal aplicada: " + tweak.Id);
                        continue;
                    }

                    TweakBackup already = FindBackup(state, tweak.Id);
                    if (already != null)
                    {
                        bool didRuntimeWork = false;
                        if (tweak.ProcessNames.Count > 0 || tweak.ProcessPrefixes.Count > 0 || tweak.ProcessPaths.Count > 0 || tweak.TemporaryServiceNameContains.Count > 0)
                        {
                            ProcessCloseResult close = _processes.CloseDetailedForTweak(tweak);
                            result.ProcessesClosed += close.ClosedProcessCount;
                            result.ServicesStopped += close.ServicesStopped;
                            didRuntimeWork = close.ClosedProcessCount > 0 || close.ServicesStopped > 0;
                        }

                        if (didRuntimeWork) result.AppliedActions++;
                        else result.NoChangeActions++;
                        result.Messages.Add(tweak.Name + ": ya estaba aplicado por ServiceKiller" + (didRuntimeWork ? " · se cerró residencia activa" : ""));
                        // V1.1.2.7: un tweak que YA estaba aplicado no debe volver a
                        // anunciar "REINICIO NECESARIO" en esta ejecución.
                        continue;
                    }

                    TweakBackup backup = new TweakBackup();
                    backup.TweakId = tweak.Id;
                    backup.TweakName = tweak.Name;
                    backup.AppliedUtc = DateTime.UtcNow;
                    bool journaled = false;

                    try
                    {
                        foreach (ServiceTarget service in tweak.Services)
                        {
                            ServiceBackup serviceBackup = _services.Capture(service.Name);
                            if (!serviceBackup.Exists)
                            {
                                _log.Info("Servicio ausente, se omite: " + service.Name);
                                continue;
                            }
                            if (service.OnlyIfAutomaticStartup && serviceBackup.StartValue != 2)
                            {
                                _log.Info("Servicio de aplicación no es Automático; no se deshabilita para preservar apertura manual: " + service.Name);
                                continue;
                            }
                            if (!_services.NeedsChange(serviceBackup, tweak.SkipManualStoppedServices))
                            {
                                _log.Info("Servicio sin cambio útil, se omite: " + service.Name);
                                continue;
                            }

                            backup.Services.Add(serviceBackup);
                            journaled = JournalBeforeChange(state, backup, journaled);
                            _services.Disable(service, serviceBackup);
                            if (serviceBackup.WasRunning && service.Stop)
                            {
                                ServiceBackup afterService = _services.Capture(service.Name);
                                if (afterService.Exists && !afterService.WasRunning)
                                    result.WindowsServicesStopped++;
                            }
                        }

                        foreach (RegistryDwordTarget regTarget in tweak.RegistryDwords)
                        {
                            RegistryValueBackup regBackup = _registry.CaptureDword(regTarget);
                            if (!_registry.NeedsDwordChange(regTarget, regBackup))
                            {
                                _log.Info("Registro ya estaba en el valor objetivo: " + regTarget.Hive + "\\" + regTarget.KeyPath + " [" + regTarget.ValueName + "]");
                                continue;
                            }

                            backup.RegistryValues.Add(regBackup);
                            journaled = JournalBeforeChange(state, backup, journaled);
                            _registry.SetDword(regTarget);
                        }

                        foreach (RegistryStringTarget regTarget in tweak.RegistryStrings)
                        {
                            RegistryValueBackup regBackup = _registry.CaptureString(regTarget);
                            if (!_registry.NeedsStringChange(regTarget, regBackup))
                            {
                                _log.Info("Registro ya estaba en el valor objetivo: " + regTarget.Hive + "\\" + regTarget.KeyPath + " [" + regTarget.ValueName + "]");
                                continue;
                            }

                            backup.RegistryValues.Add(regBackup);
                            journaled = JournalBeforeChange(state, backup, journaled);
                            _registry.SetString(regTarget);
                        }

                        foreach (StartupRule rule in tweak.StartupRules)
                        {
                            List<StartupEntryBackup> entries = _startup.FindMatches(rule);
                            if (entries.Count == 0) continue;
                            backup.StartupEntries.AddRange(entries);
                            journaled = JournalBeforeChange(state, backup, journaled);
                            _startup.RemoveEntries(entries);
                        }

                        foreach (BootTarget bootTarget in tweak.BootTargets)
                        {
                            BootValueBackup bootBackup = _boot.Capture(bootTarget.Name);
                            if (!_boot.NeedsChange(bootTarget, bootBackup))
                            {
                                _log.Info("BCD ya estaba en el valor objetivo: " + bootTarget.Name + "=" + bootTarget.TargetValue);
                                continue;
                            }

                            backup.BootValues.Add(bootBackup);
                            journaled = JournalBeforeChange(state, backup, journaled);
                            _boot.Set(bootTarget);
                        }

                        bool runtimeWork = false;
                        if (tweak.ProcessNames.Count > 0 || tweak.ProcessPrefixes.Count > 0 || tweak.ProcessPaths.Count > 0 || tweak.TemporaryServiceNameContains.Count > 0)
                        {
                            ProcessCloseResult close = _processes.CloseDetailedForTweak(tweak);
                            result.ProcessesClosed += close.ClosedProcessCount;
                            result.ServicesStopped += close.ServicesStopped;
                            runtimeWork = close.ClosedProcessCount > 0 || close.ServicesStopped > 0;
                        }

                        if (journaled)
                        {
                            // Guardado final del journal, incluyendo todos los componentes añadidos.
                            _store.Save(state);
                            result.PersistentChanges++;
                            result.AppliedActions++;
                            result.Messages.Add(tweak.Name + ": aplicado y respaldado" + (runtimeWork ? " · residencia cerrada" : ""));
                        }
                        else if (runtimeWork)
                        {
                            result.AppliedActions++;
                            result.TemporaryActions++;
                            result.Messages.Add(tweak.Name + ": configuración ya estaba en objetivo; se cerró residencia activa");
                        }
                        else
                        {
                            result.NoChangeActions++;
                            result.Messages.Add(tweak.Name + ": no necesitaba cambios");
                        }

                        if (tweak.ChangeKind == ChangeKind.RestartRequired && journaled)
                            result.RestartRequired = true;
                    }
                    catch
                    {
                        _log.Warn("Fallo dentro de " + tweak.Name + "; se intenta rollback inmediato del tweak.");
                        bool rollbackOk = TryRollback(backup);
                        if (journaled && rollbackOk)
                        {
                            state.Tweaks.Remove(backup);
                            _store.Save(state);
                        }
                        else if (journaled)
                        {
                            _store.Save(state);
                            _log.Warn("El journal se conserva porque el rollback no pudo completarse.");
                        }
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Error aplicando " + tweak.Id + ": " + ex.Message);
                    result.ErrorActions++;
                    result.Messages.Add(tweak.Name + ": ERROR - " + ex.Message);
                }
            }

            return result;
        }

        private bool JournalBeforeChange(ActiveState state, TweakBackup backup, bool journaled)
        {
            bool addedNow = false;
            if (!journaled)
            {
                state.Tweaks.Add(backup);
                journaled = true;
                addedNow = true;
            }

            try
            {
                // Write-ahead journal: el estado original queda en disco ANTES del cambio real.
                _store.Save(state);
                return journaled;
            }
            catch
            {
                if (addedNow) state.Tweaks.Remove(backup);
                throw;
            }
        }

        public List<string> RepairKnownStaleJournalEntries()
        {
            List<string> repaired = new List<string>();
            if (!PrivilegeHelper.IsAdministrator()) return repaired;
            if (_store.SafetyLocked) return repaired;

            ActiveState state = _store.Load();
            TweakBackup widgets = FindBackup(state, "win.widgets");
            if (widgets != null)
            {
                RegistryDwordTarget target = new RegistryDwordTarget
                {
                    Hive = "HKLM",
                    KeyPath = @"SOFTWARE\Policies\Microsoft\Dsh",
                    ValueName = "AllowNewsAndInterests",
                    TargetValue = 0
                };
                RegistryValueBackup current = _registry.CaptureDword(target);
                RegistryValueBackup original = widgets.RegistryValues.FirstOrDefault(delegate(RegistryValueBackup item)
                {
                    return item != null &&
                           string.Equals(item.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.KeyPath, @"SOFTWARE\Policies\Microsoft\Dsh", StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.ValueName, "AllowNewsAndInterests", StringComparison.OrdinalIgnoreCase);
                });

                // Reparación deliberadamente conservadora: solo elimina el caso demostrado
                // en V1.1.2.5, donde el valor ORIGINAL era ausente y el valor ACTUAL sigue
                // ausente. Si existe cualquier ambigüedad, se conserva el backup.
                bool provenPhantom = original != null && !original.Exists && !current.Exists;
                if (provenPhantom)
                {
                    _store.ArchiveActive("repair-stale-widgets");
                    state.Tweaks.Remove(widgets);
                    if (state.Tweaks.Count == 0) _store.ClearActive();
                    else _store.Save(state);
                    string message = "Journal autorreparado: win.widgets tenía baseline ausente y AllowNewsAndInterests sigue ausente; se eliminó únicamente esa entrada fantasma.";
                    _log.Warn(message);
                    repaired.Add(message);
                }
            }
            // V1.1.2.12: si un tweak pendiente ya coincide exactamente con su baseline
            // (por ejemplo después de reiniciar un servicio trigger-start que se restauró
            // parcialmente), el journal deja de representar una modificación activa.
            // Se limpia únicamente tras verificación componente por componente.
            List<TweakBackup> stale = state.Tweaks.ToList();
            foreach (TweakBackup candidate in stale)
            {
                string detail;
                if (!BackupMatchesCurrentState(candidate, out detail)) continue;
                _store.ArchiveActive("repair-verified-restored");
                state.Tweaks.Remove(candidate);
                string message = "Journal autorreparado: " + candidate.TweakId + " ya coincide exactamente con su estado original; entrada eliminada.";
                _log.Warn(message);
                repaired.Add(message);
            }
            if (repaired.Count > 0)
            {
                if (state.Tweaks.Count == 0) _store.ClearActive();
                else _store.Save(state);
            }

            return repaired;
        }

        public List<TweakBackup> GetActiveBackups()
        {
            return _store.Load().Tweaks;
        }

        public bool IsApplied(string tweakId)
        {
            return FindBackup(_store.Load(), tweakId) != null;
        }

        public HashSet<string> GetAppliedIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ActiveState state = _store.Load();
            foreach (TweakBackup backup in state.Tweaks) ids.Add(backup.TweakId);
            return ids;
        }

        public List<string> Restore(IEnumerable<string> tweakIds)
        {
            List<string> messages = new List<string>();
            ActiveState state = _store.Load();
            _store.ArchiveActive("before-restore");
            HashSet<string> ids = new HashSet<string>(tweakIds, StringComparer.OrdinalIgnoreCase);
            List<TweakBackup> targets = state.Tweaks.Where(delegate(TweakBackup t) { return ids.Contains(t.TweakId); }).ToList();

            foreach (TweakBackup backup in targets)
            {
                try
                {
                    RestoreBackup(backup);
                    string verifiedDetail;
                    if (!BackupMatchesCurrentState(backup, out verifiedDetail))
                        throw new InvalidOperationException("la restauración terminó pero no coincide con el backup: " + verifiedDetail);
                    state.Tweaks.Remove(backup);
                    _store.Save(state);
                    _log.Info("Tweak restaurado y verificado: " + backup.TweakId);
                    messages.Add(backup.TweakName + ": restaurado y verificado");
                }
                catch (Exception ex)
                {
                    // V1.1.2.12: una operación auxiliar puede devolver Access Denied aunque
                    // el estado final ya sea exactamente el del backup. Verificamos antes de
                    // conservar un journal aparentemente pendiente.
                    string verifiedDetail;
                    if (BackupMatchesCurrentState(backup, out verifiedDetail))
                    {
                        state.Tweaks.Remove(backup);
                        _store.Save(state);
                        _log.Warn("La restauración de " + backup.TweakId + " reportó '" + ex.Message + "', pero el estado final coincide exactamente con el backup; journal resuelto.");
                        messages.Add(backup.TweakName + ": restaurado y verificado (se ignoró un error auxiliar)");
                    }
                    else
                    {
                        _log.Error("Error restaurando " + backup.TweakId + ": " + ex.Message + " | Verificación: " + verifiedDetail);
                        messages.Add(backup.TweakName + ": ERROR - " + ex.Message + " · diferencia: " + verifiedDetail);
                    }
                }
            }

            if (state.Tweaks.Count == 0)
            {
                _store.ArchiveActive("restored");
                _store.ClearActive();
            }
            return messages;
        }

        public TweakRuntimeState GetRuntimeState(TweakDefinition tweak, HashSet<string> appliedIds)
        {
            TweakRuntimeState state = new TweakRuntimeState();
            state.IsAppliedByServiceKiller = appliedIds != null && appliedIds.Contains(tweak.Id);
            state.ApplicationInstallState = ApplicationInstallState.NotApplicable;
            state.IsActionAvailable = !tweak.IsProtectedInfo;

            if (tweak.IsProtectedInfo)
            {
                state.Summary = "Protegido";
                state.Details = "ServiceKiller no modifica este componente.";
                return state;
            }

            ApplicationPresenceResult appPresence = null;
            if (tweak.IsApplication)
            {
                appPresence = _applications.Detect(tweak);
                state.ApplicationInstallState = appPresence.State;
                state.IsApplicationRunning = appPresence.IsRunning;
                state.ApplicationProcessCount = appPresence.ProcessCount;
                state.ApplicationRootProcessCount = appPresence.RootProcessCount;
                state.ApplicationMemoryMb = appPresence.MemoryMb;
                state.IsActionAvailable = appPresence.State != ApplicationInstallState.NotInstalled;

                if (appPresence.State == ApplicationInstallState.NotInstalled)
                {
                    state.Summary = ApplicationDetector.StatusText(appPresence.State);
                    state.Details = appPresence.Details;
                    return state;
                }

                if (tweak.ChangeKind == ChangeKind.Temporary)
                {
                    state.Summary = ApplicationDetector.StatusText(appPresence.State);
                    state.Details = appPresence.Details;
                    return state;
                }
            }

            if (tweak.ChangeKind == ChangeKind.Temporary)
            {
                bool running = _processes.IsAnyRunning(tweak);
                state.Summary = running ? "Ejecutándose" : "No detectado / cerrado";
                state.Details = running ? "Hay procesos asociados activos." : "No se detectan procesos asociados.";
                return state;
            }

            if (tweak.IsStartupOnlyAction || tweak.IsCustomStartupAction ||
                (tweak.StartupRules.Count > 0 && tweak.Services.Count == 0 && tweak.RegistryDwords.Count == 0 && tweak.RegistryStrings.Count == 0 && tweak.BootTargets.Count == 0))
            {
                List<string> startupDetails = new List<string>();
                bool startupEnabled = false;

                foreach (StartupRule rule in tweak.StartupRules)
                {
                    List<StartupEntryBackup> matches = _startup.FindMatches(rule);
                    List<StartupEntryBackup> nonTaskMatches = matches.Where(delegate(StartupEntryBackup m)
                    {
                        return !string.Equals(m.EntryType, "ScheduledTask", StringComparison.OrdinalIgnoreCase);
                    }).ToList();
                    List<StartupManager.ScheduledTaskMatchInfo> taskMatches = _startup.FindScheduledTaskMatches(rule);

                    if (nonTaskMatches.Count > 0 || taskMatches.Any(delegate(StartupManager.ScheduledTaskMatchInfo t) { return t.Enabled; }))
                        startupEnabled = true;

                    if (nonTaskMatches.Count == 0 && taskMatches.Count == 0)
                    {
                        startupDetails.Add("Inicio [" + rule.MatchText + "]: no detectado");
                    }
                    else
                    {
                        foreach (StartupEntryBackup match in nonTaskMatches)
                        {
                            string mechanism = string.Equals(match.EntryType, "File", StringComparison.OrdinalIgnoreCase)
                                ? "Carpeta Inicio: " + (match.FilePath ?? match.ValueName)
                                : (match.Hive + "\\" + match.KeyPath + " [" + match.ValueName + "]");
                            startupDetails.Add("Inicio activo: " + mechanism);
                        }
                        foreach (StartupManager.ScheduledTaskMatchInfo task in taskMatches)
                        {
                            startupDetails.Add("Tarea programada: " + task.FullName + " · " + (task.Enabled ? "HABILITADA" : "DESHABILITADA"));
                        }
                    }
                }

                foreach (RegistryDwordTarget target in tweak.RegistryDwords)
                {
                    RegistryValueBackup current = _registry.CaptureDword(target);
                    bool needs = _registry.NeedsDwordChange(target, current);
                    if (needs) startupEnabled = true;
                    startupDetails.Add("StartupTask/Registro: " + _registry.DescribeDword(target) +
                                       (needs ? " -> activo/no objetivo" : " -> desactivado"));
                }

                foreach (RegistryStringTarget target in tweak.RegistryStrings)
                {
                    RegistryValueBackup current = _registry.CaptureString(target);
                    bool needs = _registry.NeedsStringChange(target, current);
                    if (needs) startupEnabled = true;
                    startupDetails.Add("StartupTask/Registro: " + _registry.DescribeString(target) +
                                       (needs ? " -> activo/no objetivo" : " -> desactivado"));
                }

                foreach (ServiceTarget service in tweak.Services)
                {
                    ServiceBackup current = _services.Capture(service.Name);
                    if (!current.Exists)
                    {
                        startupDetails.Add("Servicio " + service.Name + ": no disponible");
                        continue;
                    }
                    bool serviceAuto = current.StartValue == 2;
                    if (serviceAuto) startupEnabled = true;
                    startupDetails.Add("Servicio " + service.Name + ": " + _services.Describe(service.Name) +
                                       (serviceAuto ? " -> inicio automático activo" : (current.StartValue == 4 ? " -> inicio deshabilitado" : "")));
                }

                string installText = appPresence == null ? string.Empty :
                    (appPresence.State == ApplicationInstallState.NotVerifiable ? "NO VERIFICABLE · " : "INSTALADO · ");
                if (state.IsAppliedByServiceKiller)
                    state.Summary = installText + (startupEnabled ? "INICIO ACTIVO · REVISAR (JOURNAL SK)" : "INICIO DESACTIVADO POR SERVICEKILLER");
                else
                    state.Summary = installText + (startupEnabled ? "INICIO AUTOMÁTICO" : "SIN INICIO AUTOMÁTICO DETECTADO");

                if (appPresence != null && !string.IsNullOrWhiteSpace(appPresence.Details))
                    startupDetails.Insert(0, appPresence.Details);
                state.Details = string.Join(Environment.NewLine, startupDetails.ToArray());
                return state;
            }

            if (tweak.BootTargets.Count > 0)
            {
                BootTarget bootTarget = tweak.BootTargets[0];
                string text = _boot.Describe(bootTarget.Name);
                state.Summary = text;
                state.Details = bootTarget.Name + " = " + text;
                return state;
            }

            if (tweak.Services.Count > 0)
            {
                int existing = 0;
                int running = 0;
                int disabled = 0;
                List<string> details = new List<string>();
                foreach (ServiceTarget service in tweak.Services)
                {
                    ServiceBackup s = _services.Capture(service.Name);
                    if (!s.Exists)
                    {
                        details.Add(service.Name + ": no disponible");
                        continue;
                    }
                    existing++;
                    if (s.WasRunning) running++;
                    if (s.StartValue == 4) disabled++;
                    details.Add(service.Name + ": " + _services.Describe(service.Name));
                }

                if (existing == 0) state.Summary = "No disponible";
                else if (disabled == existing) state.Summary = "Deshabilitado";
                else if (running > 0) state.Summary = "Activo (" + running + "/" + existing + ")";
                else state.Summary = "Parado / Manual";
                state.Details = string.Join(Environment.NewLine, details.ToArray());
                return state;
            }

            if (tweak.RegistryDwords.Count > 0 || tweak.RegistryStrings.Count > 0)
            {
                List<string> values = new List<string>();
                foreach (RegistryDwordTarget target in tweak.RegistryDwords)
                    values.Add(_registry.DescribeDword(target));
                foreach (RegistryStringTarget target in tweak.RegistryStrings)
                    values.Add(_registry.DescribeString(target));
                state.Summary = state.IsAppliedByServiceKiller ? "Desactivado por ServiceKiller" : "Configuración actual";
                state.Details = string.Join(Environment.NewLine, values.ToArray());
                return state;
            }

            state.Summary = "Disponible";
            state.Details = "Sin estado específico.";
            return state;
        }

        public string Preview(TweakDefinition tweak)
        {
            List<string> parts = new List<string>();

            if (tweak.ChangeKind == ChangeKind.Temporary)
            {
                if (tweak.IsApplication)
                {
                    ApplicationPresenceResult presence = _applications.Detect(tweak);
                    if (presence.State == ApplicationInstallState.NotInstalled)
                        parts.Add("TEMPORAL: NO INSTALADO -> SIN ACCIÓN");
                    else if (presence.State == ApplicationInstallState.NotVerifiable)
                        parts.Add("TEMPORAL: instalación NO VERIFICABLE; no se detecta proceso activo");
                    else if (presence.IsRunning)
                    {
                        string resources = presence.ProcessCount > 0
                            ? " · " + presence.ProcessCount + " proceso(s), ~" + presence.MemoryMb + " MB RAM"
                            : " · residencia/servicio activo";
                        parts.Add("TEMPORAL: INSTALADO · EJECUTÁNDOSE" + resources + " -> cerrar árbol(es)");
                    }
                    else
                        parts.Add("TEMPORAL: INSTALADO · CERRADO -> sin proceso activo ahora");
                }
                else
                {
                    parts.Add("TEMPORAL: " + (_processes.IsAnyRunning(tweak) ? "aplicación/residencia detectada -> cerrar" : "no se detecta residencia activa"));
                }
            }

            foreach (ServiceTarget target in tweak.Services)
            {
                ServiceBackup current = _services.Capture(target.Name);
                if (!current.Exists)
                {
                    parts.Add("Servicio " + target.Name + ": no disponible -> SIN CAMBIO");
                    continue;
                }

                string before = _services.Describe(target.Name);
                if (!_services.NeedsChange(current, tweak.SkipManualStoppedServices))
                    parts.Add("Servicio " + target.Name + ": " + before + " -> SIN CAMBIO ÚTIL");
                else
                    parts.Add("Servicio " + target.Name + ": " + before + " -> " +
                              (target.DisableStartup ? "Inicio deshabilitado" : "Inicio sin cambio") +
                              (target.Stop ? " / Parado" : " / estado runtime se conserva"));
            }

            foreach (RegistryDwordTarget target in tweak.RegistryDwords)
            {
                RegistryValueBackup current = _registry.CaptureDword(target);
                string before = _registry.DescribeDword(target);
                if (!_registry.NeedsDwordChange(target, current))
                    parts.Add("Registro " + target.ValueName + ": " + before + " -> SIN CAMBIO");
                else
                    parts.Add("Registro " + target.ValueName + ": " + before + " -> " + target.TargetValue);
            }

            foreach (RegistryStringTarget target in tweak.RegistryStrings)
            {
                RegistryValueBackup current = _registry.CaptureString(target);
                string before = _registry.DescribeString(target);
                if (!_registry.NeedsStringChange(target, current))
                    parts.Add("Registro " + target.ValueName + ": " + before + " -> SIN CAMBIO");
                else
                    parts.Add("Registro " + target.ValueName + ": " + before + " -> \"" + target.TargetValue + "\"");
            }

            foreach (StartupRule rule in tweak.StartupRules)
            {
                int count = _startup.FindMatches(rule).Count;
                parts.Add("Inicio automático [" + rule.MatchText + "]: " + (count == 0 ? "no detectado -> SIN CAMBIO" : count + " entrada(s) -> eliminar con backup"));
            }

            foreach (BootTarget target in tweak.BootTargets)
            {
                if (!PrivilegeHelper.IsAdministrator())
                {
                    parts.Add("BCD " + target.Name + ": lectura completa requiere administrador -> se verificará al aplicar (" + target.TargetValue + ", REINICIO)");
                    continue;
                }

                BootValueBackup current = _boot.Capture(target.Name);
                string before = current.Exists ? current.Value : "Predeterminado / no explícito";
                if (!_boot.NeedsChange(target, current))
                    parts.Add("BCD " + target.Name + ": " + before + " -> SIN CAMBIO");
                else
                    parts.Add("BCD " + target.Name + ": " + before + " -> " + target.TargetValue + " (REINICIO)");
            }

            if (tweak.ChangeKind != ChangeKind.Temporary && (tweak.ProcessNames.Count > 0 || tweak.ProcessPrefixes.Count > 0 || tweak.ProcessPaths.Count > 0 || tweak.TemporaryServiceNameContains.Count > 0))
                parts.Add("Procesos asociados: " + (_processes.IsAnyRunning(tweak) ? "detectados -> cerrar" : "no detectados"));

            if (parts.Count == 0) parts.Add("Sin acciones detectadas para este componente.");
            return string.Join(Environment.NewLine, parts.ToArray());
        }

        private bool BackupMatchesCurrentState(TweakBackup backup, out string detail)
        {
            List<string> differences = new List<string>();
            if (backup == null) { detail = "backup nulo"; return false; }

            foreach (ServiceBackup service in backup.Services)
            {
                string d;
                if (!_services.MatchesBackup(service, true, out d)) differences.Add("Servicio " + service.Name + ": " + d);
            }
            foreach (RegistryValueBackup reg in backup.RegistryValues)
            {
                string d;
                if (!_registry.MatchesBackup(reg, out d)) differences.Add("Registro " + reg.Hive + "\\" + reg.KeyPath + " [" + reg.ValueName + "]: " + d);
            }
            foreach (StartupEntryBackup startup in backup.StartupEntries)
            {
                string d;
                if (!_startup.MatchesBackup(startup, out d)) differences.Add("Inicio " + (startup.ValueName ?? startup.TaskName ?? startup.FilePath) + ": " + d);
            }
            foreach (BootValueBackup boot in backup.BootValues)
            {
                BootValueBackup actual = _boot.Capture(boot.Name);
                bool ok = boot.Exists == actual.Exists && (!boot.Exists || string.Equals(boot.Value ?? string.Empty, actual.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (!ok) differences.Add("BCD " + boot.Name + ": esperado " + (boot.Exists ? boot.Value : "ausente") + ", actual " + (actual.Exists ? actual.Value : "ausente"));
            }

            detail = differences.Count == 0 ? "todos los componentes coinciden con el backup" : string.Join("; ", differences.ToArray());
            return differences.Count == 0;
        }

        private void RestoreBackup(TweakBackup backup)
        {
            // Restaurar configuración persistente primero, y servicios al final con su estado runtime original.
            for (int i = backup.BootValues.Count - 1; i >= 0; i--) _boot.Restore(backup.BootValues[i]);
            for (int i = backup.StartupEntries.Count - 1; i >= 0; i--) _startup.Restore(backup.StartupEntries[i]);
            for (int i = backup.RegistryValues.Count - 1; i >= 0; i--) _registry.Restore(backup.RegistryValues[i]);
            for (int i = backup.Services.Count - 1; i >= 0; i--) _services.Restore(backup.Services[i]);
            _log.Info("Tweak restaurado: " + backup.TweakId);
        }

        private bool TryRollback(TweakBackup backup)
        {
            try
            {
                RestoreBackup(backup);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Rollback parcial falló: " + ex.Message);
                return false;
            }
        }

        private static TweakBackup FindBackup(ActiveState state, string id)
        {
            foreach (TweakBackup backup in state.Tweaks)
                if (string.Equals(backup.TweakId, id, StringComparison.OrdinalIgnoreCase)) return backup;
            return null;
        }
    }
}
