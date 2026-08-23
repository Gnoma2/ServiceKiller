using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class CustomAppStore
    {
        private readonly Logger _log;

        public CustomAppStore(Logger log)
        {
            _log = log;
        }

        public List<CustomApplicationInfo> Load()
        {
            try { AppPaths.EnsureUser(); } catch { }

            CustomApplicationState state = TryLoad(AppPaths.CustomApps);
            if (state == null)
            {
                state = TryLoad(AppPaths.CustomAppsBackup);
                if (state != null && _log != null)
                    _log.Warn("custom-apps.json no se pudo leer; se ha usado la copia .bak.");
            }

            if (state == null || state.Applications == null)
                return new List<CustomApplicationInfo>();

            return state.Applications
                .Where(delegate(CustomApplicationInfo a)
                {
                    return a != null && IsValidId(a.Id) &&
                           !string.IsNullOrWhiteSpace(a.DisplayName) &&
                           !string.IsNullOrWhiteSpace(a.ProcessName);
                })
                .GroupBy(delegate(CustomApplicationInfo a) { return a.Id; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, CustomApplicationInfo> g) { return g.First(); })
                .ToList();
        }

        public void Save(IEnumerable<CustomApplicationInfo> applications)
        {
            AppPaths.EnsureUser();
            CustomApplicationState state = new CustomApplicationState();
            state.Applications = applications == null
                ? new List<CustomApplicationInfo>()
                : applications.Where(delegate(CustomApplicationInfo a) { return a != null; }).ToList();

            string temp = AppPaths.CustomApps + ".tmp-" + Guid.NewGuid().ToString("N");
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CustomApplicationState));
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                serializer.WriteObject(stream, state);

            try
            {
                if (File.Exists(AppPaths.CustomApps))
                {
                    try
                    {
                        File.Replace(temp, AppPaths.CustomApps, AppPaths.CustomAppsBackup, true);
                    }
                    catch
                    {
                        try { File.Copy(AppPaths.CustomApps, AppPaths.CustomAppsBackup, true); } catch { }
                        File.Copy(temp, AppPaths.CustomApps, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, AppPaths.CustomApps);
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public static TweakDefinition ToTweak(CustomApplicationInfo app)
        {
            TweakDefinition tweak = new TweakDefinition();
            tweak.Id = "custom.app." + app.Id;
            tweak.Name = app.DisplayName;
            tweak.Category = "Mis aplicaciones";
            tweak.Description = "Cierra temporalmente la aplicación personalizada cuando esta casilla está seleccionada y pulsas APLICAR.";
            tweak.Consequences = "La aplicación no se desinstala ni cambia su configuración. Puedes volver a abrirla normalmente después de jugar.";
            tweak.Impact = ImpactLevel.Low;
            tweak.PerformanceBenefit = PerformanceBenefitLevel.Low;
            tweak.ChangeKind = ChangeKind.Temporary;
            tweak.Aggressive = app.IncludeInAggressive;
            tweak.IsApplication = true;
            tweak.IsCustomApplication = true;
            tweak.CustomApplicationId = app.Id;
            tweak.CustomSourcePath = app.SourcePath;
            tweak.CustomLaunchTargetPath = app.LaunchTargetPath;
            tweak.CustomProcessName = app.ProcessName;
            tweak.CustomDetectionNote = app.DetectionNote;

            string processName = Path.GetFileNameWithoutExtension(app.ProcessName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(processName)) tweak.ProcessNames.Add(processName);
            if (!string.IsNullOrWhiteSpace(app.ProcessExecutablePath)) tweak.ProcessPaths.Add(app.ProcessExecutablePath);
            return tweak;
        }

        public static TweakDefinition ToStartupTweak(CustomApplicationInfo app)
        {
            TweakDefinition tweak = new TweakDefinition();
            tweak.Id = "custom.startup." + app.Id;
            tweak.Name = "Quitar " + app.DisplayName + " del inicio automático";
            tweak.Category = "Mis aplicaciones";
            tweak.Description = "Detecta entradas de inicio automático de esta aplicación en Run/RunOnce y carpetas Inicio y las elimina con copia reversible.";
            tweak.Consequences = "La aplicación seguirá funcionando al abrirla manualmente. ServiceKiller guarda la entrada original para poder restaurarla.";
            tweak.Impact = ImpactLevel.Low;
            tweak.PerformanceBenefit = PerformanceBenefitLevel.VeryLow;
            tweak.ChangeKind = ChangeKind.Persistent;
            tweak.Aggressive = false;
            tweak.IsApplication = true;
            tweak.IsCustomApplication = true;
            tweak.IsCustomStartupAction = true;
            tweak.IsStartupOnlyAction = true;
            tweak.CustomApplicationId = app.Id;
            tweak.CustomSourcePath = app.SourcePath;
            tweak.CustomLaunchTargetPath = app.LaunchTargetPath;
            tweak.CustomProcessName = app.ProcessName;
            tweak.CustomDetectionNote = app.DetectionNote;

            string processName = Path.GetFileNameWithoutExtension(app.ProcessName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(processName))
                tweak.StartupRules.Add(new StartupRule { MatchText = processName, SearchValueName = true, SearchValueData = true });
            return tweak;
        }

        private static bool IsValidId(string id)
        {
            Guid parsed;
            return !string.IsNullOrWhiteSpace(id) && Guid.TryParseExact(id, "N", out parsed);
        }

        private CustomApplicationState TryLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CustomApplicationState));
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return serializer.ReadObject(stream) as CustomApplicationState;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn("No se pudo leer " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}
