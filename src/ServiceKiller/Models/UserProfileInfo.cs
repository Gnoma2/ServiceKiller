using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ServiceKillerV1.Models
{
    [DataContract]
    public sealed class UserProfileState
    {
        public UserProfileState()
        {
            Version = "1.0";
            Profiles = new List<UserProfileInfo>();
        }

        [DataMember] public string Version { get; set; }
        [DataMember] public List<UserProfileInfo> Profiles { get; set; }
    }

    [DataContract]
    public sealed class UserProfileInfo
    {
        public UserProfileInfo()
        {
            TweakIds = new List<string>();
        }

        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public List<string> TweakIds { get; set; }
        [DataMember] public ApplyMode ApplyMode { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }
        [DataMember] public DateTime UpdatedUtc { get; set; }

        public override string ToString()
        {
            return Name ?? "Perfil";
        }
    }

    public sealed class ResidentProcessCandidate
    {
        public int RootPid { get; set; }
        public string DisplayName { get; set; }
        public string ProcessName { get; set; }
        public string ExecutablePath { get; set; }
        public int ProcessCount { get; set; }
        public long MemoryMb { get; set; }
        public bool HasMainWindow { get; set; }
        public string Note { get; set; }
    }
}
