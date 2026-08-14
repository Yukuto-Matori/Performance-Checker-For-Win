using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

internal static class Program
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "performance-log.yaml");

    public static async Task Main(string[] args)
    {
        var intervalSeconds = ParseInterval(args);
        var durationSeconds = ParseDuration(args);

        Console.WriteLine("Performance-Checker-For-Win");
        Console.WriteLine($"Log : {LogPath}");
        Console.WriteLine($"Interval : {intervalSeconds}s");
        Console.WriteLine("L = open YAML in Notepad, S = snapshot, Q = quit");
        Console.WriteLine();

        using var monitor = new HardwareMonitor();
        var logger = new YamlLogger(LogPath);
        var processTracker = new ProcessTracker();

        var report = new PerformanceReport
        {
            SchemaVersion = "0.1",
            StartedAt = DateTimeOffset.Now,
            Host = HardwareCollector.CollectHostInfo(monitor),
            Samples = new List<PerformanceSample>()
        };

        var previousNetwork = NetworkCollector.Capture();
        var previousDisk = DiskCollector.Capture();
        processTracker.Capture();

        AddSample(report, monitor, processTracker, previousNetwork, previousDisk);
        await logger.WriteAsync(report);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var start = Stopwatch.GetTimestamp();
        var nextSample = DateTimeOffset.UtcNow.AddSeconds(intervalSeconds);

        while (!cts.IsCancellationRequested)
        {
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;
                switch (key)
                {
                    case ConsoleKey.L:
                        OpenInNotepad(LogPath);
                        break;
                    case ConsoleKey.S:
                        AddSample(report, monitor, processTracker, previousNetwork, previousDisk);
                        await logger.WriteAsync(report);
                        Console.WriteLine($"[{DateTime.Now:T}] snapshot saved");
                        break;
                    case ConsoleKey.Q:
                        cts.Cancel();
                        break;
                }
            }

            if (durationSeconds > 0 && Stopwatch.GetElapsedTime(start).TotalSeconds >= durationSeconds)
                break;

            if (DateTimeOffset.UtcNow >= nextSample)
            {
                AddSample(report, monitor, processTracker, previousNetwork, previousDisk);
                await logger.WriteAsync(report);
                Console.WriteLine($"[{DateTime.Now:T}] sample #{report.Samples.Count} saved");
                nextSample = DateTimeOffset.UtcNow.AddSeconds(intervalSeconds);
            }

            await Task.Delay(100, cts.Token).ContinueWith(_ => { }, CancellationToken.None);
        }

        report.EndedAt = DateTimeOffset.Now;
        report.Summary = SummaryCollector.Create(report);
        await logger.WriteAsync(report);
        Console.WriteLine("Monitoring stopped.");
    }

    private static void AddSample(
        PerformanceReport report,
        HardwareMonitor monitor,
        ProcessTracker processTracker,
        NetworkSnapshot previousNetwork,
        DiskSnapshot previousDisk)
    {
        var networkNow = NetworkCollector.Capture();
        var diskNow = DiskCollector.Capture();
        var processes = processTracker.Capture();
        var sensors = monitor.ReadSensors();

        report.Samples.Add(new PerformanceSample
        {
            Timestamp = DateTimeOffset.Now,
            Cpu = sensors.Cpu,
            Memory = MemoryCollector.Capture(),
            TopProcesses = processes,
            Network = NetworkCollector.Calculate(previousNetwork, networkNow),
            Disk = DiskCollector.Calculate(previousDisk, diskNow),
            Gpu = sensors.Gpu
        });

        previousNetwork.UpdateFrom(networkNow);
        previousDisk.UpdateFrom(diskNow);
    }

    private static void OpenInNotepad(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open Notepad: {ex.Message}");
        }
    }

    private static int ParseInterval(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--interval", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var value))
                return Math.Max(1, value);
        return 60;
    }

    private static int ParseDuration(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--duration", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var value))
                return Math.Max(0, value);
        return 0;
    }
}

internal sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        _computer.Open();
    }

    public SensorSnapshot ReadSensors()
    {
        var cpu = new CpuMetrics();
        var gpus = new List<GpuMetrics>();

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Update();

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    cpu.UsagePercent = Find(hardware, SensorType.Load, "CPU Total") ?? Find(hardware, SensorType.Load, "CPU Package") ?? FindAny(hardware, SensorType.Load);
                    cpu.TemperatureC = Find(hardware, SensorType.Temperature, "CPU Package") ?? Find(hardware, SensorType.Temperature, "Core Max") ?? FindAny(hardware, SensorType.Temperature);
                    cpu.PowerWatts = Find(hardware, SensorType.Power, "CPU Package") ?? FindAny(hardware, SensorType.Power);
                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    gpus.Add(new GpuMetrics
                    {
                        Name = hardware.Name,
                        UsagePercent = Find(hardware, SensorType.Load, "GPU Core") ?? FindAny(hardware, SensorType.Load),
                        TemperatureC = Find(hardware, SensorType.Temperature, "GPU Core") ?? FindAny(hardware, SensorType.Temperature),
                        MemoryUsedPercent = Find(hardware, SensorType.SmallData, "GPU Memory Used") ?? Find(hardware, SensorType.Load, "GPU Memory"),
                        PowerWatts = Find(hardware, SensorType.Power, "GPU Power") ?? FindAny(hardware, SensorType.Power)
                    });
                    break;
            }
        }

        return new SensorSnapshot { Cpu = cpu, Gpu = gpus };
    }

    private static float? Find(IHardware hardware, SensorType type, string contains)
    {
        return hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(contains, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static float? FindAny(IHardware hardware, SensorType type)
    {
        return hardware.Sensors.FirstOrDefault(s => s.SensorType == type)?.Value;
    }

    public void Dispose() => _computer.Close();
}

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware) => hardware.Traverse(this);
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

internal static class HardwareCollector
{
    public static HostInfo CollectHostInfo(HardwareMonitor monitor)
    {
        var cpu = QueryCpu();
        var gpus = QueryGpus();
        var disks = QueryDisks();

        return new HostInfo
        {
            ComputerName = Environment.MachineName,
            Os = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            Cpu = cpu,
            Gpus = gpus,
            Memory = new MemoryInfo { TotalBytes = MemoryCollector.GetTotalBytes() },
            Disks = disks,
            LogicalDrives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new LogicalDriveInfo
                {
                    Name = d.Name,
                    FileSystem = d.DriveFormat,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.AvailableFreeSpace
                }).ToList()
        };
    }

    private static CpuInfo QueryCpu()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        return new CpuInfo
        {
            Name = item?["Name"]?.ToString(),
            Cores = ConvertToInt(item?["NumberOfCores"]),
            LogicalProcessors = ConvertToInt(item?["NumberOfLogicalProcessors"]),
            MaxClockMHz = ConvertToInt(item?["MaxClockSpeed"])
        };
    }

    private static List<GpuInfo> QueryGpus()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
        return searcher.Get().Cast<ManagementObject>().Select(item => new GpuInfo
        {
            Name = item["Name"]?.ToString(),
            DriverVersion = item["DriverVersion"]?.ToString(),
            VramBytes = ConvertToUInt64(item["AdapterRAM"]),
            PnpDeviceId = item["PNPDeviceID"]?.ToString()
        }).ToList();
    }

    private static List<PhysicalDiskInfo> QueryDisks()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Model, SerialNumber, InterfaceType, MediaType, Size, Status FROM Win32_DiskDrive");
        return searcher.Get().Cast<ManagementObject>().Select(item => new PhysicalDiskInfo
        {
            Model = item["Model"]?.ToString(),
            SerialNumber = item["SerialNumber"]?.ToString()?.Trim(),
            InterfaceType = item["InterfaceType"]?.ToString(),
            MediaType = item["MediaType"]?.ToString(),
            SizeBytes = ConvertToUInt64(item["Size"]),
            Status = item["Status"]?.ToString()
        }).ToList();
    }

    private static int? ConvertToInt(object? value) => value is null ? null : Convert.ToInt32(value);
    private static ulong? ConvertToUInt64(object? value) => value is null ? null : Convert.ToUInt64(value);
}

internal static class MemoryCollector
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    public static ulong GetTotalBytes()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : 0;
    }

    public static MemoryMetrics Capture()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return new MemoryMetrics();
        return new MemoryMetrics
        {
            UsagePercent = status.dwMemoryLoad,
            TotalBytes = status.ullTotalPhys,
            AvailableBytes = status.ullAvailPhys,
            UsedBytes = status.ullTotalPhys - status.ullAvailPhys
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
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
    }
}

internal sealed class ProcessTracker
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset Time)> _previous = new();

    public List<ProcessMetrics> Capture()
    {
        var now = DateTimeOffset.UtcNow;
        var logicalProcessors = Environment.ProcessorCount;
        var results = new List<ProcessMetrics>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var cpu = process.TotalProcessorTime;
                var value = 0.0;
                if (_previous.TryGetValue(process.Id, out var previous))
                {
                    var elapsed = (now - previous.Time).TotalSeconds;
                    if (elapsed > 0)
                        value = Math.Max(0, (cpu - previous.Cpu).TotalSeconds / elapsed / logicalProcessors * 100.0);
                }
                _previous[process.Id] = (cpu, now);
                results.Add(new ProcessMetrics
                {
                    Name = process.ProcessName,
                    Pid = process.Id,
                    CpuPercent = value,
                    MemoryBytes = process.WorkingSet64
                });
            }
            catch
            {
                // Process may terminate between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }

        return results.OrderByDescending(p => p.CpuPercent).Take(3).ToList();
    }
}

internal static class NetworkCollector
{
    public static NetworkSnapshot Capture()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.OperationalStatus == OperationalStatus.Up)
            .Select(i => i.GetIPStatistics())
            .ToList();
        return new NetworkSnapshot
        {
            ReceivedBytes = interfaces.Sum(i => i.BytesReceived),
            SentBytes = interfaces.Sum(i => i.BytesSent)
        };
    }

    public static NetworkMetrics Calculate(NetworkSnapshot previous, NetworkSnapshot current)
    {
        var seconds = Math.Max(0.001, (current.Timestamp - previous.Timestamp).TotalSeconds);
        return new NetworkMetrics
        {
            ReceiveBytesPerSec = Math.Max(0, current.ReceivedBytes - previous.ReceivedBytes) / seconds,
            SendBytesPerSec = Math.Max(0, current.SentBytes - previous.SentBytes) / seconds,
            TotalReceivedBytes = current.ReceivedBytes,
            TotalSentBytes = current.SentBytes
        };
    }
}

internal sealed class NetworkSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public long ReceivedBytes { get; set; }
    public long SentBytes { get; set; }

    public void UpdateFrom(NetworkSnapshot other)
    {
        Timestamp = other.Timestamp;
        ReceivedBytes = other.ReceivedBytes;
        SentBytes = other.SentBytes;
    }
}

internal static class DiskCollector
{
    public static DiskSnapshot Capture()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, DiskReadBytesPerSec, DiskWriteBytesPerSec, PercentDiskTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name <> '_Total'");
        var disks = new List<DiskActivity>();
        foreach (ManagementObject item in searcher.Get())
        {
            disks.Add(new DiskActivity
            {
                Name = item["Name"]?.ToString(),
                ReadBytesPerSec = ConvertToDouble(item["DiskReadBytesPerSec"]),
                WriteBytesPerSec = ConvertToDouble(item["DiskWriteBytesPerSec"]),
                BusyPercent = ConvertToDouble(item["PercentDiskTime"])
            });
        }
        return new DiskSnapshot { Disks = disks };
    }

    public static DiskMetrics Calculate(DiskSnapshot previous, DiskSnapshot current)
    {
        return new DiskMetrics
        {
            ReadBytesPerSec = current.Disks.Sum(d => d.ReadBytesPerSec),
            WriteBytesPerSec = current.Disks.Sum(d => d.WriteBytesPerSec),
            BusyPercentMax = current.Disks.Count == 0 ? null : current.Disks.Max(d => d.BusyPercent)
        };
    }

    private static double ConvertToDouble(object? value) => value is null ? 0 : Convert.ToDouble(value);
}

internal sealed class DiskSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public List<DiskActivity> Disks { get; set; } = new();
    public void UpdateFrom(DiskSnapshot other) { Timestamp = other.Timestamp; Disks = other.Disks; }
}

internal sealed class YamlLogger
{
    private const string AiComment = """# AI_README
# この YAML は Windows PC の安定性・性能分析用ログです。
# host は起動時に取得したハードウェア情報、samples は時系列計測値です。
# CPU/GPU の temperature は摂氏、usage は概ね 0-100%、memory/disk/network は bytes 系です。
# topProcesses は各サンプル時点で CPU 使用率の高い上位3プロセスです。
# 値が null のセンサーは、そのPCまたは実行権限では取得できなかったことを意味します。
# 異常調査では、CPU温度・CPU使用率・Top Process・RAM使用率・GPU/VRAM・Disk I/O の時間相関を確認してください。
# 特に Intel 13/14世代 CPU の不安定性を疑う場合は、今後追加する WHEA-Logger / Application Error / Display / Disk / stornvme 等のイベント情報を重視してください。
# 新旧PC比較では、同じ負荷・同じ時間帯・同じサンプリング間隔のログを比較してください。
# このコメントは AI に貼り付けて分析する際の読み方を定義するためのものです。
""";

    private readonly string _path;
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .Build();

    public YamlLogger(string path) => _path = path;

    public async Task WriteAsync(PerformanceReport report)
    {
        var yaml = AiComment + Environment.NewLine + _serializer.Serialize(report);
        await File.WriteAllTextAsync(_path, yaml);
    }
}

internal static class SummaryCollector
{
    public static SummaryInfo Create(PerformanceReport report)
    {
        var samples = report.Samples;
        return new SummaryInfo
        {
            SampleCount = samples.Count,
            CpuUsageAverage = Average(samples.Select(s => s.Cpu.UsagePercent)),
            CpuTemperatureMax = Max(samples.Select(s => s.Cpu.TemperatureC)),
            MemoryUsageMax = Max(samples.Select(s => (double?)s.Memory.UsagePercent)),
            NetworkReceiveMaxBytesPerSec = Max(samples.Select(s => (double?)s.Network.ReceiveBytesPerSec)),
            NetworkSendMaxBytesPerSec = Max(samples.Select(s => (double?)s.Network.SendBytesPerSec)),
            DiskReadMaxBytesPerSec = Max(samples.Select(s => (double?)s.Disk.ReadBytesPerSec)),
            DiskWriteMaxBytesPerSec = Max(samples.Select(s => (double?)s.Disk.WriteBytesPerSec))
        };
    }

    private static double? Average(IEnumerable<float?> values)
    {
        var x = values.Where(v => v.HasValue).Select(v => (double)v!.Value).ToArray();
        return x.Length == 0 ? null : x.Average();
    }

    private static double? Max(IEnumerable<double?> values)
    {
        var x = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return x.Length == 0 ? null : x.Max();
    }

    private static double? Max(IEnumerable<float?> values) => Max(values.Select(v => v.HasValue ? (double?)v.Value : null));
}

internal sealed class PerformanceReport
{
    public string SchemaVersion { get; set; } = "0.1";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public HostInfo Host { get; set; } = new();
    public List<PerformanceSample> Samples { get; set; } = new();
    public SummaryInfo? Summary { get; set; }
}

internal sealed class HostInfo
{
    public string? ComputerName { get; set; }
    public string? Os { get; set; }
    public string? OsArchitecture { get; set; }
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<PhysicalDiskInfo> Disks { get; set; } = new();
    public List<LogicalDriveInfo> LogicalDrives { get; set; } = new();
}

internal sealed class CpuInfo
{
    public string? Name { get; set; }
    public int? Cores { get; set; }
    public int? LogicalProcessors { get; set; }
    public int? MaxClockMHz { get; set; }
}

internal sealed class GpuInfo
{
    public string? Name { get; set; }
    public string? DriverVersion { get; set; }
    public ulong? VramBytes { get; set; }
    public string? PnpDeviceId { get; set; }
}

internal sealed class MemoryInfo { public ulong TotalBytes { get; set; } }
internal sealed class PhysicalDiskInfo
{
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? InterfaceType { get; set; }
    public string? MediaType { get; set; }
    public ulong? SizeBytes { get; set; }
    public string? Status { get; set; }
}
internal sealed class LogicalDriveInfo
{
    public string? Name { get; set; }
    public string? FileSystem { get; set; }
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
}

internal sealed class PerformanceSample
{
    public DateTimeOffset Timestamp { get; set; }
    public CpuMetrics Cpu { get; set; } = new();
    public MemoryMetrics Memory { get; set; } = new();
    public List<ProcessMetrics> TopProcesses { get; set; } = new();
    public NetworkMetrics Network { get; set; } = new();
    public DiskMetrics Disk { get; set; } = new();
    public List<GpuMetrics> Gpu { get; set; } = new();
}

internal sealed class CpuMetrics
{
    public float? UsagePercent { get; set; }
    public float? TemperatureC { get; set; }
    public float? PowerWatts { get; set; }
}
internal sealed class GpuMetrics
{
    public string? Name { get; set; }
    public float? UsagePercent { get; set; }
    public float? TemperatureC { get; set; }
    public float? MemoryUsedPercent { get; set; }
    public float? PowerWatts { get; set; }
}
internal sealed class MemoryMetrics
{
    public uint UsagePercent { get; set; }
    public ulong TotalBytes { get; set; }
    public ulong UsedBytes { get; set; }
    public ulong AvailableBytes { get; set; }
}
internal sealed class ProcessMetrics
{
    public string? Name { get; set; }
    public int Pid { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
}
internal sealed class NetworkMetrics
{
    public double ReceiveBytesPerSec { get; set; }
    public double SendBytesPerSec { get; set; }
    public long TotalReceivedBytes { get; set; }
    public long TotalSentBytes { get; set; }
}
internal sealed class DiskMetrics
{
    public double ReadBytesPerSec { get; set; }
    public double WriteBytesPerSec { get; set; }
    public double? BusyPercentMax { get; set; }
}
internal sealed class DiskActivity
{
    public string? Name { get; set; }
    public double ReadBytesPerSec { get; set; }
    public double WriteBytesPerSec { get; set; }
    public double BusyPercent { get; set; }
}
internal sealed class SensorSnapshot
{
    public CpuMetrics Cpu { get; set; } = new();
    public List<GpuMetrics> Gpu { get; set; } = new();
}
internal sealed class SummaryInfo
{
    public int SampleCount { get; set; }
    public double? CpuUsageAverage { get; set; }
    public double? CpuTemperatureMax { get; set; }
    public double? MemoryUsageMax { get; set; }
    public double? NetworkReceiveMaxBytesPerSec { get; set; }
    public double? NetworkSendMaxBytesPerSec { get; set; }
    public double? DiskReadMaxBytesPerSec { get; set; }
    public double? DiskWriteMaxBytesPerSec { get; set; }
}
