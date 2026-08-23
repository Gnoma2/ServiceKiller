using System;
using System.Collections.Generic;

namespace ServiceKillerV1.Models
{
    public sealed class TweakDefinition
    {
        public TweakDefinition()
        {
            Services = new List<ServiceTarget>();
            RegistryDwords = new List<RegistryDwordTarget>();
            RegistryStrings = new List<RegistryStringTarget>();
            BootTargets = new List<BootTarget>();
            ProcessNames = new List<string>();
            ProcessPrefixes = new List<string>();
            ProcessPaths = new List<string>();
            TemporaryServiceNameContains = new List<string>();
            StartupRules = new List<StartupRule>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string Consequences { get; set; }
        public ImpactLevel Impact { get; set; }
        public PerformanceBenefitLevel PerformanceBenefit { get; set; }
        public ChangeKind ChangeKind { get; set; }
        public bool Conservative { get; set; }
        public bool Balanced { get; set; }
        public bool Aggressive { get; set; }
        public bool IsProtectedInfo { get; set; }
        public bool IsApplication { get; set; }
        public bool IsCustomApplication { get; set; }
        public bool IsCustomStartupAction { get; set; }
        // Acción cuyo único objetivo es evitar que una aplicación arranque en el próximo logon.
        // Nunca se ejecuta en el modo temporal "hasta reinicio", aunque internamente use
        // Registro, tareas programadas o un servicio residente.
        public bool IsStartupOnlyAction { get; set; }
        public string CustomApplicationId { get; set; }
        public string CustomSourcePath { get; set; }
        public string CustomLaunchTargetPath { get; set; }
        public string CustomProcessName { get; set; }
        public string CustomDetectionNote { get; set; }
        public bool SkipManualStoppedServices { get; set; }
        public List<ServiceTarget> Services { get; private set; }
        public List<RegistryDwordTarget> RegistryDwords { get; private set; }
        public List<RegistryStringTarget> RegistryStrings { get; private set; }
        public List<BootTarget> BootTargets { get; private set; }
        public List<string> ProcessNames { get; private set; }
        public List<string> ProcessPrefixes { get; private set; }
        public List<string> ProcessPaths { get; private set; }
        public List<string> TemporaryServiceNameContains { get; private set; }
        public List<StartupRule> StartupRules { get; private set; }

        public bool IsSelectedByPreset(PresetKind preset)
        {
            if (preset == PresetKind.Conservative) return Conservative;
            if (preset == PresetKind.Balanced) return Balanced;
            if (preset == PresetKind.Aggressive) return Aggressive;
            return false;
        }

        // En modo "hasta reinicio" solo se aplican cambios que tienen efecto útil
        // durante la sesión actual. Un cambio BCD/Hyper-V necesita precisamente un
        // reinicio para empezar a actuar, y quitar un programa del inicio automático
        // solo afecta al siguiente logon; ambos quedan fuera de este modo.
        public bool SupportsUntilRestartMode()
        {
            if (IsProtectedInfo) return false;
            if (IsStartupOnlyAction || IsCustomStartupAction) return false;
            if (ChangeKind == ChangeKind.Temporary) return true;
            if (ChangeKind == ChangeKind.RestartRequired || BootTargets.Count > 0) return false;
            if (StartupRules.Count > 0 && Services.Count == 0 && RegistryDwords.Count == 0 && RegistryStrings.Count == 0 && BootTargets.Count == 0) return false;
            return true;
        }
    }

    public sealed class ServiceTarget
    {
        public string Name { get; set; }
        public bool Stop { get; set; }
        public bool DisableStartup { get; set; }
        // Para servicios auxiliares de aplicaciones: solo cambia el tipo de inicio si ya era Automático.
        // Evita convertir un servicio Manual (necesario al abrir la app a mano) en Deshabilitado.
        public bool OnlyIfAutomaticStartup { get; set; }
    }

    public sealed class RegistryDwordTarget
    {
        public string Hive { get; set; }
        public string KeyPath { get; set; }
        public string ValueName { get; set; }
        public int TargetValue { get; set; }
    }

    public sealed class RegistryStringTarget
    {
        public string Hive { get; set; }
        public string KeyPath { get; set; }
        public string ValueName { get; set; }
        public string TargetValue { get; set; }
    }

    public sealed class BootTarget
    {
        public string Name { get; set; }
        public string TargetValue { get; set; }
    }

    public sealed class StartupRule
    {
        public string MatchText { get; set; }
        public bool SearchValueName { get; set; }
        public bool SearchValueData { get; set; }
    }
}
