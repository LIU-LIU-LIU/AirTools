using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AirTools.Tools.SystemMonitor.Services
{
    public class SystemMonitorService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        public class NetworkStats
        {
            public double UploadSpeed { get; set; }
            public double DownloadSpeed { get; set; }
            public string AdapterName { get; set; } = "";
        }

        public class SystemStats
        {
            public double CpuUsage { get; set; }
            public double MemoryUsage { get; set; }
            public double TotalMemoryGB { get; set; }
            public double UsedMemoryGB { get; set; }
            public double DiskReadSpeed { get; set; }
            public double DiskWriteSpeed { get; set; }
            public NetworkStats Network { get; set; } = new();
        }

        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _diskReadCounter;
        private PerformanceCounter? _diskWriteCounter;
        private int _updateIntervalMs = 1000;
        private SystemStats _lastStats = new();
        private long _lastUpdateTime;
        private long _lastBytesIn;
        private long _lastBytesOut;
        private string _currentAdapter = "";
        private bool _firstCpuRead = true;
        private string _diskDrive = "C";
        private string _networkAdapterId = "";

        public int UpdateIntervalMs
        {
            get => _updateIntervalMs;
            set => _updateIntervalMs = Math.Max(500, Math.Min(10000, value));
        }

        public string NetworkAdapterId
        {
            get => _networkAdapterId;
            set => _networkAdapterId = value;
        }

        public string DiskDrive
        {
            get => _diskDrive;
            set
            {
                var val = string.IsNullOrEmpty(value) ? "C" : value.TrimEnd(':', '\\').ToUpperInvariant();
                if (_diskDrive != val)
                {
                    _diskDrive = val;
                    InitializeDiskCounters();
                }
            }
        }

        public SystemMonitorService()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch { _cpuCounter = null; }
            InitializeDiskCounters();
        }

        private void InitializeDiskCounters()
        {
            try
            {
                _diskReadCounter?.Dispose();
                _diskWriteCounter?.Dispose();
                _diskReadCounter = new PerformanceCounter("LogicalDisk", "Disk Read Bytes/sec", _diskDrive + ":");
                _diskWriteCounter = new PerformanceCounter("LogicalDisk", "Disk Write Bytes/sec", _diskDrive + ":");
                _diskReadCounter.NextValue();
                _diskWriteCounter.NextValue();
            }
            catch 
            { 
                _diskReadCounter = null; 
                _diskWriteCounter = null; 
            }
        }

        public SystemStats GetStats()
        {
            var now = Environment.TickCount64;
            if (now - _lastUpdateTime < _updateIntervalMs && _lastUpdateTime > 0)
                return _lastStats;

            UpdateCpu();
            UpdateMemory();
            UpdateDisk();
            UpdateNetwork();

            _lastUpdateTime = now;
            return _lastStats;
        }

        private void UpdateCpu()
        {
            if (_cpuCounter == null) return;
            try
            {
                var value = _cpuCounter.NextValue();
                if (_firstCpuRead) { _firstCpuRead = false; _lastStats.CpuUsage = 0; }
                else _lastStats.CpuUsage = Math.Min(100, Math.Max(0, value));
            }
            catch { _lastStats.CpuUsage = 0; }
        }

        private void UpdateMemory()
        {
            try
            {
                var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref mem))
                {
                    _lastStats.MemoryUsage = Math.Min(100, mem.MemoryLoad);
                    _lastStats.TotalMemoryGB = mem.TotalPhys / (1024.0 * 1024 * 1024);
                    _lastStats.UsedMemoryGB = (mem.TotalPhys - mem.AvailPhys) / (1024.0 * 1024 * 1024);
                }
            }
            catch 
            { 
                _lastStats.MemoryUsage = 0; 
                _lastStats.TotalMemoryGB = 0;
                _lastStats.UsedMemoryGB = 0;
            }
        }

        private void UpdateDisk()
        {
            try
            {
                if (_diskReadCounter != null && _diskWriteCounter != null)
                {
                    _lastStats.DiskReadSpeed = _diskReadCounter.NextValue();
                    _lastStats.DiskWriteSpeed = _diskWriteCounter.NextValue();
                }
                else
                {
                    _lastStats.DiskReadSpeed = 0;
                    _lastStats.DiskWriteSpeed = 0;
                }
            }
            catch 
            { 
                _lastStats.DiskReadSpeed = 0; 
                _lastStats.DiskWriteSpeed = 0; 
            }
        }

        private void UpdateNetwork()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .ToList();

                NetworkInterface? best = null;

                if (!string.IsNullOrEmpty(_networkAdapterId))
                {
                    best = interfaces.FirstOrDefault(n => n.Id == _networkAdapterId);
                }

                if (best == null)
                {
                    long maxThroughput = 0;
                    foreach (var ni in interfaces)
                    {
                        var stats = ni.GetIPStatistics();
                        var total = stats.BytesReceived + stats.BytesSent;
                        if (total > maxThroughput) { maxThroughput = total; best = ni; }
                    }
                }

                if (best == null) return;

                var stats2 = best.GetIPStatistics();
                var bytesIn = stats2.BytesReceived;
                var bytesOut = stats2.BytesSent;
                var name = best.Name;

                if (_currentAdapter != name)
                {
                    _currentAdapter = name;
                    _lastBytesIn = bytesIn;
                    _lastBytesOut = bytesOut;
                    _lastStats.Network.DownloadSpeed = 0;
                    _lastStats.Network.UploadSpeed = 0;
                }
                else
                {
                    var timeDiff = (Environment.TickCount64 - _lastUpdateTime) / 1000.0;
                    if (timeDiff > 0)
                    {
                        _lastStats.Network.DownloadSpeed = (bytesIn - _lastBytesIn) / timeDiff;
                        _lastStats.Network.UploadSpeed = (bytesOut - _lastBytesOut) / timeDiff;
                    }
                }

                _lastBytesIn = bytesIn;
                _lastBytesOut = bytesOut;
                _lastStats.Network.AdapterName = name;
            }
            catch
            {
                _lastStats.Network.DownloadSpeed = 0;
                _lastStats.Network.UploadSpeed = 0;
            }
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
        }
    }
}
