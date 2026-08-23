using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class WindowsServiceManager
    {
        private readonly Logger _log;

        public WindowsServiceManager(Logger log)
        {
            _log = log;
        }

        public bool Exists(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    ServiceControllerStatus ignored = sc.Status;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public ServiceBackup Capture(string serviceName)
        {
            ServiceBackup backup = new ServiceBackup();
            backup.Name = serviceName;
            backup.Exists = Exists(serviceName);
            if (!backup.Exists) return backup;

            string path = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, false))
            {
                if (key == null)
                {
                    backup.Exists = false;
                    return backup;
                }

                object start = key.GetValue("Start", 3);
                backup.StartValue = Convert.ToInt32(start);
                object delayed = key.GetValue("DelayedAutoStart", null);
                backup.DelayedAutoStartExists = delayed != null;
                backup.DelayedAutoStart = delayed == null ? 0 : Convert.ToInt32(delayed);
            }

            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    backup.WasRunning = sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
                }
            }
            catch { }

            return backup;
        }

        public bool NeedsChange(ServiceBackup backup, bool skipManualStopped)
        {
            if (backup == null || !backup.Exists) return false;
            if (skipManualStopped && backup.StartValue == 3 && !backup.WasRunning) return false;
            if (backup.StartValue == 4 && !backup.WasRunning) return false;
            return true;
        }

        public void Disable(ServiceTarget target, ServiceBackup backup)
        {
            if (backup == null || !backup.Exists) return;

            if (target.Stop) StopService(target.Name);

            if (target.DisableStartup && backup.StartValue != 4)
            {
                SetServiceStartType(target.Name, 4);
            }

            _log.Info("Servicio optimizado: " + target.Name);
        }

        public void Restore(ServiceBackup backup)
        {
            if (backup == null || !backup.Exists) return;
            if (!Exists(backup.Name))
                throw new InvalidOperationException("El servicio que existía al crear el backup ya no existe: " + backup.Name);

            SetServiceStartType(backup.Name, backup.StartValue);

            // DelayedAutoStart no forma parte de ChangeServiceConfig; solo tocamos su
            // valor de Registro si realmente difiere del backup.
            ServiceBackup current = Capture(backup.Name);
            bool delayedMatches = current.Exists &&
                current.DelayedAutoStartExists == backup.DelayedAutoStartExists &&
                (!backup.DelayedAutoStartExists || current.DelayedAutoStart == backup.DelayedAutoStart);
            if (!delayedMatches)
            {
                string path = @"SYSTEM\CurrentControlSet\Services\" + backup.Name;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, true))
                {
                    if (key == null) throw new InvalidOperationException("No se pudo abrir la clave de servicio para restaurar DelayedAutoStart: " + backup.Name);
                    if (backup.DelayedAutoStartExists)
                        key.SetValue("DelayedAutoStart", backup.DelayedAutoStart, RegistryValueKind.DWord);
                    else
                        key.DeleteValue("DelayedAutoStart", false);
                }
            }

            if (backup.WasRunning) StartServiceStrict(backup.Name);
            else StopServiceStrict(backup.Name);

            string detail;
            if (!MatchesBackup(backup, true, out detail))
                throw new InvalidOperationException("Restauración no confirmada para " + backup.Name + ": " + detail);

            _log.Info("Servicio restaurado y verificado: " + backup.Name + " -> Start=" + backup.StartValue + ", Running=" + backup.WasRunning);
        }

        public bool MatchesBackup(ServiceBackup expected, bool compareRunning, out string detail)
        {
            detail = string.Empty;
            if (expected == null) { detail = "backup nulo"; return false; }
            ServiceBackup actual = Capture(expected.Name);
            if (expected.Exists != actual.Exists) { detail = "existencia distinta"; return false; }
            if (!expected.Exists) { detail = "servicio ausente como en el backup"; return true; }
            if (expected.StartValue != actual.StartValue) { detail = "Start esperado " + expected.StartValue + ", actual " + actual.StartValue; return false; }
            if (expected.DelayedAutoStartExists != actual.DelayedAutoStartExists) { detail = "presencia de DelayedAutoStart distinta"; return false; }
            if (expected.DelayedAutoStartExists && expected.DelayedAutoStart != actual.DelayedAutoStart) { detail = "DelayedAutoStart esperado " + expected.DelayedAutoStart + ", actual " + actual.DelayedAutoStart; return false; }
            if (compareRunning && expected.WasRunning != actual.WasRunning) { detail = "estado esperado " + (expected.WasRunning ? "Ejecutándose" : "Parado") + ", actual " + (actual.WasRunning ? "Ejecutándose" : "Parado"); return false; }
            detail = "coincide con el backup";
            return true;
        }

        public string Describe(string serviceName)
        {
            ServiceBackup state = Capture(serviceName);
            if (!state.Exists) return "No disponible";
            string start = StartValueText(state.StartValue, state.DelayedAutoStartExists && state.DelayedAutoStart == 1);
            return start + (state.WasRunning ? " / Ejecutándose" : " / Parado");
        }

        public int CountRunningServices()
        {
            int count = 0;
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController service in services)
                {
                    try
                    {
                        if (service.Status == ServiceControllerStatus.Running) count++;
                    }
                    catch { }
                    service.Dispose();
                }
            }
            catch { }
            return count;
        }

        public bool IsAnyServiceContainingRunning(string text)
        {
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController service in services)
                {
                    try
                    {
                        if ((service.ServiceName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             service.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) &&
                            service.Status == ServiceControllerStatus.Running)
                            return true;
                    }
                    catch { }
                    finally { service.Dispose(); }
                }
            }
            catch { }
            return false;
        }

        public List<string> StopServicesContaining(string text)
        {
            List<string> stopped = new List<string>();
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController service in services)
                {
                    string name = service.ServiceName;
                    try
                    {
                        bool matches = service.ServiceName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       service.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!matches || service.Status != ServiceControllerStatus.Running) continue;

                        if (!service.CanStop)
                        {
                            _log.Info("Servicio residente no admite parada temporal y se deja intacto: " + name);
                            continue;
                        }

                        try
                        {
                            service.Stop();
                            try { service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(6)); } catch { }
                            service.Refresh();
                            if (service.Status == ServiceControllerStatus.Stopped)
                            {
                                stopped.Add(name);
                                _log.Info("Servicio temporal detenido: " + name);
                                continue;
                            }
                        }
                        catch (Exception firstEx)
                        {
                            _log.Warn("No se pudo detener servicio temporal " + name + " mediante ServiceController: " + firstEx.Message);
                            continue;
                        }

                        _log.Warn("El servicio temporal siguió ejecutándose tras solicitar su parada: " + name);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("No se pudo comprobar/detener servicio temporal " + name + ": " + ex.Message);
                    }
                    finally
                    {
                        service.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudieron enumerar servicios para cierre temporal: " + ex.Message);
            }
            return stopped;
        }

        private void StopService(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    sc.Refresh();
                    if ((sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.Paused) && sc.CanStop)
                    {
                        sc.Stop();
                        try { sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(6)); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo detener " + serviceName + ": " + ex.Message);
            }
        }

        private void StartService(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    sc.Refresh();
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        try { sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8)); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo devolver a Running " + serviceName + ": " + ex.Message);
            }
        }


        private void StopServiceStrict(string serviceName)
        {
            using (ServiceController sc = new ServiceController(serviceName))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                if (!sc.CanStop) throw new InvalidOperationException("El servicio no se puede detener ahora: " + serviceName);
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Stopped)
                    throw new InvalidOperationException("El servicio no volvió al estado Parado: " + serviceName);
            }
        }

        private void StartServiceStrict(string serviceName)
        {
            using (ServiceController sc = new ServiceController(serviceName))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Running) return;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(12));
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Running)
                    throw new InvalidOperationException("El servicio no volvió al estado Ejecutándose: " + serviceName);
            }
        }

        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceChangeConfig = 0x0002;
        private const uint ServiceNoChange = 0xFFFFFFFF;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig(
            IntPtr service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password,
            string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        private static void SetServiceStartType(string serviceName, int startValue)
        {
            if (startValue < 0 || startValue > 4)
                throw new InvalidOperationException("Start desconocido: " + startValue);

            IntPtr manager = IntPtr.Zero;
            IntPtr service = IntPtr.Zero;
            try
            {
                manager = OpenSCManager(null, null, ScManagerConnect);
                if (manager == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo abrir Service Control Manager.");

                service = OpenService(manager, serviceName, ServiceChangeConfig);
                if (service == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo abrir el servicio " + serviceName + " para cambiar su inicio.");

                if (!ChangeServiceConfig(service, ServiceNoChange, Convert.ToUInt32(startValue), ServiceNoChange,
                    null, null, IntPtr.Zero, null, null, null, null))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rechazó el cambio de inicio del servicio " + serviceName + ".");
            }
            finally
            {
                if (service != IntPtr.Zero) CloseServiceHandle(service);
                if (manager != IntPtr.Zero) CloseServiceHandle(manager);
            }
        }

        private static string StartValueText(int startValue, bool delayed)
        {
            if (startValue == 0) return "Boot";
            if (startValue == 1) return "System";
            if (startValue == 2 && delayed) return "Automático (retrasado)";
            if (startValue == 2) return "Automático";
            if (startValue == 3) return "Manual";
            if (startValue == 4) return "Deshabilitado";
            return "Inicio " + startValue;
        }
    }
}
