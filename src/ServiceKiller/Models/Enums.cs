namespace ServiceKillerV1.Models
{
    public enum ImpactLevel
    {
        Low,
        Medium,
        High
    }

    // Estimación cualitativa del ahorro potencial de actividad en segundo plano.
    // No representa FPS garantizados ni una medición de benchmark.
    public enum PerformanceBenefitLevel
    {
        None,
        VeryLow,
        Low,
        Medium,
        High
    }

    public enum ApplicationInstallState
    {
        NotApplicable,
        InstalledRunning,
        InstalledClosed,
        NotInstalled,
        NotVerifiable
    }

    public enum ChangeKind
    {
        Persistent,
        Temporary,
        RestartRequired
    }

    public enum PresetKind
    {
        Conservative,
        Balanced,
        Aggressive,
        Custom
    }

    // Cómo debe vivir el boost seleccionado. Persistent conserva el comportamiento
    // clásico; UntilRestart registra un journal separado y programa su restauración
    // automática para el próximo inicio de sesión tras reiniciar/cerrar sesión.
    public enum ApplyMode
    {
        Persistent,
        UntilRestart
    }
}
