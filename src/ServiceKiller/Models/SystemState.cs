using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ServiceKillerV1.Models
{
    [DataContract]
    public sealed class ActiveState
    {
        public ActiveState()
        {
            Version = "1.0";
            Tweaks = new List<TweakBackup>();
        }

        [DataMember] public string Version { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }
        [DataMember] public string MachineName { get; set; }
        [DataMember] public string UserName { get; set; }
        // Campos opcionales V1.02.8. Los journals anteriores siguen siendo compatibles.
        [DataMember(EmitDefaultValue = false)] public string StatePurpose { get; set; }
        [DataMember(EmitDefaultValue = false)] public string OriginUserSid { get; set; }
        [DataMember(EmitDefaultValue = false)] public string OriginAccountName { get; set; }
        [DataMember] public List<TweakBackup> Tweaks { get; set; }
    }

    [DataContract]
    public sealed class TweakBackup
    {
        public TweakBackup()
        {
            Services = new List<ServiceBackup>();
            RegistryValues = new List<RegistryValueBackup>();
            StartupEntries = new List<StartupEntryBackup>();
            BootValues = new List<BootValueBackup>();
        }

        [DataMember] public string TweakId { get; set; }
        [DataMember] public string TweakName { get; set; }
        [DataMember] public DateTime AppliedUtc { get; set; }
        [DataMember] public List<ServiceBackup> Services { get; set; }
        [DataMember] public List<RegistryValueBackup> RegistryValues { get; set; }
        [DataMember] public List<StartupEntryBackup> StartupEntries { get; set; }
        [DataMember] public List<BootValueBackup> BootValues { get; set; }
    }

    [DataContract]
    public sealed class ServiceBackup
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public bool Exists { get; set; }
        [DataMember] public int StartValue { get; set; }
        [DataMember] public bool DelayedAutoStartExists { get; set; }
        [DataMember] public int DelayedAutoStart { get; set; }
        [DataMember] public bool WasRunning { get; set; }
    }

    [DataContract]
    public sealed class RegistryValueBackup
    {
        [DataMember] public string Hive { get; set; }
        [DataMember] public string KeyPath { get; set; }
        [DataMember] public string ValueName { get; set; }
        [DataMember] public bool Exists { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public string StringData { get; set; }
        [DataMember] public string[] StringArrayData { get; set; }
        [DataMember] public byte[] BinaryData { get; set; }
        [DataMember] public long IntegerData { get; set; }
    }

    [DataContract]
    public sealed class StartupEntryBackup
    {
        public StartupEntryBackup()
        {
            StartupApprovals = new List<StartupApprovalBackup>();
        }

        [DataMember] public string Hive { get; set; }
        [DataMember] public string RegistryView { get; set; }
        [DataMember] public string KeyPath { get; set; }
        [DataMember] public string ValueName { get; set; }
        [DataMember] public string ValueData { get; set; }
        [DataMember] public string ValueKind { get; set; }
        [DataMember] public string EntryType { get; set; }
        [DataMember] public string FilePath { get; set; }
        [DataMember] public string BackupPath { get; set; }
        // V1.1.2.5: soporte reversible para tareas programadas que arrancan al iniciar sesión.
        [DataMember(EmitDefaultValue = false)] public string TaskPath { get; set; }
        [DataMember(EmitDefaultValue = false)] public string TaskName { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool TaskWasEnabled { get; set; }
        // V1.1.2.12: Windows mantiene una segunda capa de activación de elementos Run
        // en Explorer\StartupApproved. Se guarda exactamente (incluida ausencia) para
        // que RESTAURAR recupere el arranque efectivo, no solo la entrada Run.
        [DataMember(EmitDefaultValue = false)] public List<StartupApprovalBackup> StartupApprovals { get; set; }
    }

    [DataContract]
    public sealed class StartupApprovalBackup
    {
        [DataMember] public string Hive { get; set; }
        [DataMember] public string RegistryView { get; set; }
        [DataMember] public string KeyPath { get; set; }
        [DataMember] public string ValueName { get; set; }
        [DataMember] public bool Exists { get; set; }
        [DataMember] public string ValueKind { get; set; }
        [DataMember] public byte[] BinaryData { get; set; }
        [DataMember] public string StringData { get; set; }
        [DataMember] public long IntegerData { get; set; }
    }

    [DataContract]
    public sealed class BootValueBackup
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public bool Exists { get; set; }
        [DataMember] public string Value { get; set; }
    }

    public sealed class TweakRuntimeState
    {
        public string Summary { get; set; }
        public string Details { get; set; }
        public bool IsAppliedByServiceKiller { get; set; }
        public bool IsSessionApplied { get; set; }
        public ApplicationInstallState ApplicationInstallState { get; set; }
        public bool IsActionAvailable { get; set; }
        public bool IsApplicationRunning { get; set; }
        // V1.03: métricas de la aplicación/proceso en el instante del refresco.
        // Se usan solo para informar; nunca condicionan la seguridad del journal.
        public int ApplicationProcessCount { get; set; }
        public int ApplicationRootProcessCount { get; set; }
        public long ApplicationMemoryMb { get; set; }
    }

    public sealed class SystemMetrics
    {
        public int RunningServices { get; set; }
        public int Processes { get; set; }
        public long TotalMemoryMb { get; set; }
        public long AvailableMemoryMb { get; set; }
        public long UsedMemoryMb { get; set; }
    }
}
