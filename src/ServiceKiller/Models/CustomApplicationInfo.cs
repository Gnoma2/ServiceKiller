using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ServiceKillerV1.Models
{
    [DataContract]
    public sealed class CustomApplicationState
    {
        public CustomApplicationState()
        {
            Version = "1.0";
            Applications = new List<CustomApplicationInfo>();
        }

        [DataMember] public string Version { get; set; }
        [DataMember] public List<CustomApplicationInfo> Applications { get; set; }
    }

    [DataContract]
    public sealed class CustomApplicationInfo
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string DisplayName { get; set; }
        [DataMember] public string SourcePath { get; set; }
        [DataMember] public string LaunchTargetPath { get; set; }
        [DataMember] public string ProcessExecutablePath { get; set; }
        [DataMember] public string ProcessName { get; set; }
        [DataMember] public string ShortcutArguments { get; set; }
        [DataMember] public string DetectionNote { get; set; }
        [DataMember] public DateTime AddedUtc { get; set; }
        [DataMember] public bool IncludeInAggressive { get; set; }
    }
}
