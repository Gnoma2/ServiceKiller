using System;
using System.IO;
using System.Runtime.Serialization.Json;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class StateStore
    {
        private readonly Logger _log;
        private readonly string _activeState;
        private readonly string _recoveryState;
        private readonly string _archiveTag;

        public bool SafetyLocked { get; private set; }
        public string SafetyMessage { get; private set; }
        public string ActiveStatePath { get { return _activeState; } }

        public StateStore(Logger log)
            : this(log, AppPaths.ActiveState, "persistent")
        {
        }

        public StateStore(Logger log, string activeStatePath, string archiveTag)
        {
            _log = log;
            _activeState = string.IsNullOrWhiteSpace(activeStatePath) ? AppPaths.ActiveState : activeStatePath;
            _recoveryState = _activeState + ".bak";
            _archiveTag = string.IsNullOrWhiteSpace(archiveTag) ? "state" : archiveTag;
            // Abrir la GUI no debe crear/escribir nada en ProgramData.
            // Las carpetas de máquina solo se crean al hacer una operación elevada.
        }

        public ActiveState Load()
        {
            if (!File.Exists(_activeState)) return CreateNew();

            try
            {
                return ReadState(_activeState);
            }
            catch (UnauthorizedAccessException ex)
            {
                SafetyLocked = true;
                SafetyMessage = "El journal " + Path.GetFileName(_activeState) + " existe pero no puede leerse con los permisos actuales. Por seguridad no se interpreta como un estado limpio.";
                _log.Warn(SafetyMessage + " " + ex.Message);
                return CreateNew();
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo leer " + Path.GetFileName(_activeState) + ": " + ex.Message);
                string damaged = Path.Combine(AppPaths.Backups, Path.GetFileNameWithoutExtension(_activeState) + "-DAMAGED-" + Stamp() + ".json");
                try { AppPaths.EnsureMachine(); File.Copy(_activeState, damaged, true); MachineDataSecurity.ProtectFile(damaged); } catch { }

                if (File.Exists(_recoveryState))
                {
                    try
                    {
                        ActiveState recovered = ReadState(_recoveryState);
                        _log.Warn("Se recuperó el journal desde " + Path.GetFileName(_recoveryState) + ".");
                        try { File.Copy(_recoveryState, _activeState, true); MachineDataSecurity.ProtectFile(_activeState); } catch { }
                        return recovered;
                    }
                    catch (Exception recoveryEx)
                    {
                        _log.Error("También falló el journal de recuperación: " + recoveryEx.Message);
                    }
                }
                SafetyLocked = true;
                SafetyMessage = "El journal " + Path.GetFileName(_activeState) + " está dañado y no se pudo recuperar automáticamente. Por seguridad, ServiceKiller bloquea nuevos cambios que dependan de ese journal para no sobrescribir el estado de restauración.";
                _log.Error(SafetyMessage);
                return CreateNew();
            }
        }

        public void Save(ActiveState state)
        {
            if (SafetyLocked) throw new InvalidOperationException(SafetyMessage);
            AppPaths.EnsureMachine();
            string temp = _activeState + ".tmp";
            using (FileStream fs = File.Create(temp))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ActiveState));
                serializer.WriteObject(fs, state);
                fs.Flush(true);
            }

            if (File.Exists(_activeState))
            {
                try
                {
                    File.Replace(temp, _activeState, _recoveryState, true);
                }
                catch
                {
                    File.Copy(_activeState, _recoveryState, true);
                    File.Copy(temp, _activeState, true);
                    File.Delete(temp);
                }
            }
            else
            {
                File.Move(temp, _activeState);
            }

            // Defensa adicional: los journals se consumen después desde workers elevados.
            // Se vuelve a aplicar la ACL explícita tras cada reemplazo/copia.
            MachineDataSecurity.ProtectFile(_activeState);
            if (File.Exists(_recoveryState)) MachineDataSecurity.ProtectFile(_recoveryState);
        }

        public void ArchiveActive(string reason)
        {
            if (!File.Exists(_activeState)) return;
            try { AppPaths.EnsureMachine(); } catch { }
            string safeReason = string.IsNullOrEmpty(reason) ? "archive" : reason.Replace(' ', '-');
            string target = Path.Combine(AppPaths.Backups, "state-" + _archiveTag + "-" + Stamp() + "-" + safeReason + ".json");
            try { File.Copy(_activeState, target, true); MachineDataSecurity.ProtectFile(target); }
            catch (Exception ex) { _log.Warn("No se pudo crear el archivo histórico " + target + ": " + ex.Message); }
        }

        public void ClearActive()
        {
            try { if (File.Exists(_activeState)) File.Delete(_activeState); }
            catch (Exception ex) { _log.Warn("No se pudo borrar " + Path.GetFileName(_activeState) + " vacío: " + ex.Message); }
            try { if (File.Exists(_recoveryState)) File.Delete(_recoveryState); }
            catch (Exception ex) { _log.Warn("No se pudo borrar el journal de recuperación vacío: " + ex.Message); }
        }

        public bool ExistsOnDisk()
        {
            return File.Exists(_activeState);
        }

        private static ActiveState ReadState(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ActiveState));
                ActiveState state = serializer.ReadObject(fs) as ActiveState;
                if (state == null) throw new InvalidDataException("El journal no contiene un estado válido.");
                JournalValidator.ValidateAndNormalize(state);
                return state;
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        }

        private static ActiveState CreateNew()
        {
            ActiveState state = new ActiveState();
            state.CreatedUtc = DateTime.UtcNow;
            state.MachineName = Environment.MachineName;
            state.UserName = Environment.UserName;
            return state;
        }
    }
}
