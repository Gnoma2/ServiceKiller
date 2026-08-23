using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ServiceKillerV1.Core
{
    // Protege los datos de máquina que posteriormente pueden ser consumidos por un
    // worker elevado. Los usuarios normales pueden leerlos para diagnóstico/estado,
    // pero solo SYSTEM y Administradores pueden modificarlos.
    internal static class MachineDataSecurity
    {
        private static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier AdministratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier UsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        public static void ProtectMachineTree(string root, params string[] requiredDirectories)
        {
            if (!PrivilegeHelper.IsAdministrator())
                throw new UnauthorizedAccessException("La protección de los datos de máquina requiere administrador.");

            Directory.CreateDirectory(root);
            ApplyDirectoryAcl(root);

            if (requiredDirectories != null)
            {
                foreach (string directory in requiredDirectories)
                {
                    if (string.IsNullOrWhiteSpace(directory)) continue;
                    Directory.CreateDirectory(directory);
                    ApplyDirectoryAcl(directory);
                }
            }

            // Endurece también restos de versiones previas que pudieran haber heredado
            // ACL más permisivas de ProgramData.
            try
            {
                foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    try { ApplyDirectoryAcl(directory); } catch { }
                }
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { ProtectFile(file); } catch { }
                }
            }
            catch
            {
                // La creación de las carpetas principales y sus ACL ya se ha completado.
                // Un resto individual inaccesible no debe impedir reparar el resto del árbol.
            }
        }

        public static void ProtectFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (!PrivilegeHelper.IsAdministrator())
                throw new UnauthorizedAccessException("La protección de archivos de máquina requiere administrador.");

            FileSecurity security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);
            security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(UsersSid, FileSystemRights.ReadAndExecute | FileSystemRights.Read, AccessControlType.Allow));
            File.SetAccessControl(path, security);
        }

        // Solo se usa DESPUÉS de una restauración temporal completada y de haber
        // eliminado la tarea. Permite que la cuenta de origen borre los restos inertes
        // del worker protegido en el siguiente arranque normal de ServiceKiller, sin
        // convertir el área de SessionRestore en escribible antes de la restauración.
        public static void AllowFileDeletionBySid(string path, string sid)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (!PrivilegeHelper.IsAdministrator())
                throw new UnauthorizedAccessException("Modificar la ACL de un archivo de máquina requiere administrador.");
            if (string.IsNullOrWhiteSpace(sid))
                throw new ArgumentException("SID vacío.", "sid");

            SecurityIdentifier userSid = new SecurityIdentifier(sid);
            FileSecurity security = File.GetAccessControl(path);
            security.AddAccessRule(new FileSystemAccessRule(userSid, FileSystemRights.Delete, AccessControlType.Allow));
            File.SetAccessControl(path, security);
        }

        private static void ApplyDirectoryAcl(string path)
        {
            DirectorySecurity security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);

            InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(UsersSid, FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory | FileSystemRights.Read, inherit, PropagationFlags.None, AccessControlType.Allow));

            Directory.SetAccessControl(path, security);
        }
    }
}
