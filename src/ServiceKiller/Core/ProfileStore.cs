using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class ProfileStore
    {
        private readonly Logger _log;

        public ProfileStore(Logger log)
        {
            _log = log;
        }

        public List<UserProfileInfo> Load()
        {
            try { AppPaths.EnsureUser(); } catch { }
            UserProfileState state = TryLoad(AppPaths.Profiles);
            if (state == null)
            {
                state = TryLoad(AppPaths.ProfilesBackup);
                if (state != null && _log != null) _log.Warn("profiles.json no se pudo leer; se ha usado la copia .bak.");
            }

            if (state == null || state.Profiles == null) return new List<UserProfileInfo>();
            return state.Profiles
                .Where(delegate(UserProfileInfo p) { return p != null && IsValidId(p.Id) && !string.IsNullOrWhiteSpace(p.Name); })
                .GroupBy(delegate(UserProfileInfo p) { return p.Id; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, UserProfileInfo> g) { return Normalize(g.First()); })
                .OrderBy(delegate(UserProfileInfo p) { return p.Name; }, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void Save(IEnumerable<UserProfileInfo> profiles)
        {
            AppPaths.EnsureUser();
            UserProfileState state = new UserProfileState();
            state.Profiles = profiles == null ? new List<UserProfileInfo>() : profiles.Where(delegate(UserProfileInfo p) { return p != null; }).Select(Normalize).ToList();

            string temp = AppPaths.Profiles + ".tmp-" + Guid.NewGuid().ToString("N");
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(UserProfileState));
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                serializer.WriteObject(stream, state);

            try
            {
                if (File.Exists(AppPaths.Profiles))
                {
                    try { File.Replace(temp, AppPaths.Profiles, AppPaths.ProfilesBackup, true); }
                    catch
                    {
                        try { File.Copy(AppPaths.Profiles, AppPaths.ProfilesBackup, true); } catch { }
                        File.Copy(temp, AppPaths.Profiles, true);
                        File.Delete(temp);
                    }
                }
                else File.Move(temp, AppPaths.Profiles);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private UserProfileState TryLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(UserProfileState));
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return serializer.ReadObject(stream) as UserProfileState;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn("No se pudo leer " + path + ": " + ex.Message);
                return null;
            }
        }

        private static UserProfileInfo Normalize(UserProfileInfo p)
        {
            if (p.TweakIds == null) p.TweakIds = new List<string>();
            p.TweakIds = p.TweakIds.Where(delegate(string id) { return !string.IsNullOrWhiteSpace(id); }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (p.CreatedUtc == default(DateTime)) p.CreatedUtc = DateTime.UtcNow;
            if (p.UpdatedUtc == default(DateTime)) p.UpdatedUtc = p.CreatedUtc;
            return p;
        }

        private static bool IsValidId(string id)
        {
            Guid parsed;
            return !string.IsNullOrWhiteSpace(id) && Guid.TryParseExact(id, "N", out parsed);
        }
    }
}
