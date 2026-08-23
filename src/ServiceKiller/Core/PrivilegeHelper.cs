using System;
using System.Security.Principal;

namespace ServiceKillerV1.Core
{
    public static class PrivilegeHelper
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static string CurrentUserSid()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return identity.User == null ? string.Empty : identity.User.Value;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string CurrentAccountName()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return identity.Name ?? string.Empty;
            }
            catch { return string.Empty; }
        }

    }
}
