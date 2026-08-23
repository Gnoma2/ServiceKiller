using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.Core
{
    public sealed class SystemMetricsReader
    {
        private readonly WindowsServiceManager _services;

        public SystemMetricsReader(WindowsServiceManager services)
        {
            _services = services;
        }

        public SystemMetrics Read()
        {
            SystemMetrics metrics = new SystemMetrics();
            metrics.RunningServices = _services.CountRunningServices();
            try
            {
                Process[] processes = Process.GetProcesses();
                metrics.Processes = processes.Length;
                foreach (Process p in processes) p.Dispose();
            }
            catch { }

            MEMORYSTATUSEX memory = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memory))
            {
                metrics.TotalMemoryMb = (long)(memory.ullTotalPhys / 1024UL / 1024UL);
                long avail = (long)(memory.ullAvailPhys / 1024UL / 1024UL);
                metrics.AvailableMemoryMb = avail;
                metrics.UsedMemoryMb = metrics.TotalMemoryMb - avail;
            }
            return metrics;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }
}
