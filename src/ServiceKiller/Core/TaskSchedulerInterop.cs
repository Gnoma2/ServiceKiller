using System;
using System.Runtime.InteropServices;

namespace ServiceKillerV1.Core
{
    // Wrapper mínimo de Task Scheduler 2.0 mediante COM.
    // V1.1.2.15 usa la cuenta del usuario que activó el modo temporal con
    // privilegio "Highest available". No crea tareas SYSTEM ni ejecuta puentes
    // temporales para modificar el Registro.
    internal static class TaskSchedulerInterop
    {
        private const int TaskActionExec = 0;
        private const int TaskTriggerLogon = 9;
        private const int TaskCreateOrUpdate = 6;
        private const int TaskLogonInteractiveToken = 3;
        private const int TaskRunLevelHighest = 1;
        private const int TaskInstancesIgnoreNew = 2;
        private const int DaclSecurityInformation = 0x00000004;
        // DACL explícita: SYSTEM y Administradores tienen control total. Task Scheduler
        // añade automáticamente al principal de la tarea el acceso mínimo de lectura
        // necesario para una tarea registrada con InteractiveToken.
        private const string ProtectedTaskSddl = "D:P(A;;FA;;;SY)(A;;FA;;;BA)";

        public static void RegisterSessionRestoreTask(
            string taskName,
            string triggerUser,
            string executablePath,
            string arguments,
            string description)
        {
            object serviceObject = null;
            object rootObject = null;
            object definitionObject = null;
            object registeredObject = null;

            try
            {
                dynamic service = CreateService(out serviceObject);
                service.Connect();

                dynamic root = service.GetFolder("\\");
                rootObject = root;

                dynamic definition = service.NewTask(0);
                definitionObject = definition;

                definition.RegistrationInfo.Description = description ?? string.Empty;
                ConfigureCommonSettings(definition, "PT10M");
                ConfigureUserPrincipal(definition, triggerUser);

                dynamic action = definition.Actions.Create(TaskActionExec);
                action.Path = executablePath;
                action.Arguments = arguments ?? string.Empty;

                dynamic trigger = definition.Triggers.Create(TaskTriggerLogon);
                trigger.UserId = triggerUser;
                trigger.Delay = "PT8S";
                trigger.Enabled = true;

                dynamic registered = root.RegisterTaskDefinition(
                    NormalizeRootTaskName(taskName),
                    definition,
                    TaskCreateOrUpdate,
                    triggerUser,
                    null,
                    TaskLogonInteractiveToken,
                    ProtectedTaskSddl);
                registeredObject = registered;
            }
            finally
            {
                ReleaseCom(registeredObject);
                ReleaseCom(definitionObject);
                ReleaseCom(rootObject);
                ReleaseCom(serviceObject);
            }
        }

        public static bool TaskExists(string taskName)
        {
            object serviceObject = null;
            object rootObject = null;
            object taskObject = null;
            try
            {
                dynamic service = CreateService(out serviceObject);
                service.Connect();
                dynamic root = service.GetFolder("\\");
                rootObject = root;
                dynamic task = root.GetTask(NormalizeFullTaskPath(taskName));
                taskObject = task;
                return task != null;
            }
            catch (COMException)
            {
                return false;
            }
            catch (Exception ex)
            {
                // En algunas versiones de Windows, GetTask sobre una tarea que acaba
                // de eliminarse puede exponerse por el binder COM como una excepción
                // .NET con HRESULT ERROR_FILE_NOT_FOUND (0x80070002), no como COMException.
                // La ausencia de la tarea es el resultado esperado de TaskExists().
                if (IsExpectedTaskMissing(ex)) return false;
                throw;
            }
            finally
            {
                ReleaseCom(taskObject);
                ReleaseCom(rootObject);
                ReleaseCom(serviceObject);
            }
        }

        public static string GetTaskXml(string taskName)
        {
            object serviceObject = null;
            object rootObject = null;
            object taskObject = null;
            try
            {
                dynamic service = CreateService(out serviceObject);
                service.Connect();
                dynamic root = service.GetFolder("\\");
                rootObject = root;
                dynamic task = root.GetTask(NormalizeFullTaskPath(taskName));
                taskObject = task;
                return Convert.ToString(task.Xml) ?? string.Empty;
            }
            finally
            {
                ReleaseCom(taskObject);
                ReleaseCom(rootObject);
                ReleaseCom(serviceObject);
            }
        }


        public static string GetTaskSecurityDescriptor(string taskName)
        {
            object serviceObject = null;
            object rootObject = null;
            object taskObject = null;
            try
            {
                dynamic service = CreateService(out serviceObject);
                service.Connect();
                dynamic root = service.GetFolder("\\");
                rootObject = root;
                dynamic task = root.GetTask(NormalizeFullTaskPath(taskName));
                taskObject = task;
                return Convert.ToString(task.GetSecurityDescriptor(DaclSecurityInformation)) ?? string.Empty;
            }
            finally
            {
                ReleaseCom(taskObject);
                ReleaseCom(rootObject);
                ReleaseCom(serviceObject);
            }
        }

        public static void SetTaskEnabled(string fullTaskName, bool enabled)
        {
            object serviceObject = null;
            object rootObject = null;
            object taskObject = null;
            try
            {
                dynamic service = CreateService(out serviceObject);
                service.Connect();
                dynamic root = service.GetFolder("\\");
                rootObject = root;
                dynamic task = root.GetTask(NormalizeFullTaskPath(fullTaskName));
                taskObject = task;
                task.Enabled = enabled;
            }
            finally
            {
                ReleaseCom(taskObject);
                ReleaseCom(rootObject);
                ReleaseCom(serviceObject);
            }
        }

        public static bool DeleteTask(string taskName)
        {
            object serviceObject = null;
            object rootObject = null;
            try
            {
                dynamic service = CreateService(out serviceObject);
                service.Connect();
                dynamic root = service.GetFolder("\\");
                rootObject = root;
                root.DeleteTask(NormalizeRootTaskName(taskName), 0);
                return true;
            }
            catch (COMException ex)
            {
                if (IsExpectedTaskMissing(ex)) return false;
                throw;
            }
            catch (Exception ex)
            {
                if (IsExpectedTaskMissing(ex)) return false;
                throw;
            }
            finally
            {
                ReleaseCom(rootObject);
                ReleaseCom(serviceObject);
            }
        }

        private static dynamic CreateService(out object serviceObject)
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
                throw new InvalidOperationException("Task Scheduler 2.0 COM no está disponible en este sistema.");

            serviceObject = Activator.CreateInstance(serviceType);
            if (serviceObject == null)
                throw new InvalidOperationException("No se pudo iniciar Task Scheduler 2.0 COM.");
            return serviceObject;
        }

        private static void ConfigureUserPrincipal(dynamic definition, string userName)
        {
            dynamic principal = definition.Principal;
            principal.UserId = userName;
            principal.LogonType = TaskLogonInteractiveToken;
            principal.RunLevel = TaskRunLevelHighest;
        }

        private static void ConfigureCommonSettings(dynamic definition, string executionTimeLimit)
        {
            dynamic settings = definition.Settings;
            settings.Enabled = true;
            settings.StartWhenAvailable = true;
            settings.AllowDemandStart = true;
            settings.DisallowStartIfOnBatteries = false;
            settings.StopIfGoingOnBatteries = false;
            settings.ExecutionTimeLimit = executionTimeLimit;
            settings.MultipleInstances = TaskInstancesIgnoreNew;
        }

        private static bool IsExpectedTaskMissing(Exception ex)
        {
            const int ErrorFileNotFoundHResult = unchecked((int)0x80070002);
            Exception current = ex;
            while (current != null)
            {
                if (current.HResult == ErrorFileNotFoundHResult) return true;
                current = current.InnerException;
            }
            return false;
        }

        private static string NormalizeRootTaskName(string taskName)
        {
            string value = (taskName ?? string.Empty).Trim();
            while (value.StartsWith("\\", StringComparison.Ordinal)) value = value.Substring(1);
            return value;
        }

        private static string NormalizeFullTaskPath(string taskName)
        {
            string value = (taskName ?? string.Empty).Trim();
            if (value.Length == 0) return "\\";
            if (!value.StartsWith("\\", StringComparison.Ordinal)) value = "\\" + value;
            return value;
        }

        private static void ReleaseCom(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }
}
