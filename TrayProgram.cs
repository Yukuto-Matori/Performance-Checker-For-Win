using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using YamlDotNet.Serialization;

internal static class TrayProgram
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--cli", StringComparison.OrdinalIgnoreCase)))
        {
            NativeMethods.AllocConsole();
            try
            {
                Program.Main(args.Where(a => !a.Equals("--cli", StringComparison.OrdinalIgnoreCase)).ToArray())
                    .GetAwaiter().GetResult();
            }
            finally
            {
                NativeMethods.FreeConsole();
            }
            return;
        }

        using var mutex = new Mutex(true, "PerformanceCheckerForWin.Singleton", out var created);
        if (!created)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication());
    }
}

internal sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _monitoringItem;
    private readonly MonitoringSession _session;

    public TrayApplication()
    {
        _session = new MonitoringSession();
        _monitoringItem = new ToolStripMenuItem("監視: ON") { Checked = true, CheckOnClick = true };
        _monitoringItem.Click += (_, _) =>
        {
            if (_monitoringItem.Checked)
            {
                _session.Start();
                _monitoringItem.Text = "監視: ON";
            }
            else
            {
                _session.Stop();
                _monitoringItem.Text = "監視: OFF";
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("ログ表示", null, (_, _) => _session.OpenLog()));
        menu.Items.Add(_monitoringItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("アプリ＆監視終了", null, (_, _) => ExitApplication()));

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Performance Checker for Win",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => _session.OpenLog();

        _session.Start();
    }

    private void ExitApplication()
    {
        _session.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Stop();
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class MonitoringSession : IDisposable
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "performance-log.yaml");
    private readonly object _gate = new();
    private readonly YamlSerializer _serializer = new();
    private HardwareMonitor? _hardwareMonitor;
    private ProcessTracker? _processTracker;
    private GuiReport? _report;
    private NetworkSnapshot? _previousNetwork;
    private DiskSnapshot? _previousDisk;
    private Timer? _timer;
    private bool _started;

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
                return;

            if (_report is null)
            {
                _hardwareMonitor = new HardwareMonitor();
                _processTracker = new ProcessTracker();
                _report = new GuiReport
                {
                    SchemaVersion = "0.2",
                    StartedAt = DateTimeOffset.Now,
                    Host = HardwareCollector.CollectHostInfo(_hardwareMonitor),
                    Samples = new List<GuiSample>()
                };
                _previousNetwork = NetworkCollector.Capture();
                _previousDisk = DiskCollector.Capture();
            }

            _started = true;
            CaptureSample();
            _timer = new Timer(_ => CaptureSample(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
                return;
            _started = false;
            _timer?.Dispose();
            _timer = null;
            WriteLog();
        }
    }

    public void OpenLog()
    {
        try
        {
            if (!File.Exists(LogPath))
                WriteLog();

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{LogPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ログを開けませんでした。\n\n{ex.Message}", "Performance Checker for Win", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CaptureSample()
    {
        lock (_gate)
        {
            if (!_started || _hardwareMonitor is null || _processTracker is null || _report is null || _previousNetwork is null || _previousDisk is null)
                return;

            try
            {
                var networkNow = NetworkCollector.Capture();
                var diskNow = DiskCollector.Capture();
                var sensors = _hardwareMonitor.ReadSensors();
                var processes = _processTracker.Capture();

                _report.Samples.Add(new GuiSample
                {
                    Timestamp = DateTimeOffset.Now,
                    Cpu = sensors.Cpu,
                    Memory = MemoryCollector.Capture(),
                    TopProcesses = processes,
                    Network = NetworkCollector.Calculate(_previousNetwork, networkNow),
                    Disk = DiskCollector.Calculate(_previousDisk, diskNow),
                    Gpu = sensors.Gpu
                });

                _previousNetwork.UpdateFrom(networkNow);
                _previousDisk.UpdateFrom(diskNow);
                WriteLog();
            }
            catch (Exception ex)
            {
                _report.Errors.Add(new MonitorError { Timestamp = DateTimeOffset.Now, Message = ex.ToString() });
                WriteLog();
            }
        }
    }

    private void WriteLog()
    {
        if (_report is null)
            return;

        try
        {
            _report.UpdatedAt = DateTimeOffset.Now;
            var yaml = _serializer.Serialize(_report);
            var text = AiHeader + Environment.NewLine + yaml;
            var temp = LogPath + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, LogPath, true);
        }
        catch
        {
            // Logging must never terminate monitoring because the file can be temporarily locked by Notepad.
        }
    }

    public void Dispose()
    {
        Stop();
        _hardwareMonitor?.Dispose();
    }

    private const string AiHeader = "# AI_README\n# Purpose: Windows PC stability/performance diagnostic log.\n# host: hardware information captured once when this application first starts.\n# samples: time-series measurements captured immediately at start and every 60 seconds while monitoring is ON.\n# CPU/GPU temperatures are degrees Celsius. Percent values are percentages. Byte-rate values are bytes per second.\n# topProcesses contains the three processes consuming the most CPU during the latest sampling interval.\n# Compare average/max temperature, CPU/GPU utilization, memory pressure, disk I/O and network traffic.\n# For crash investigation, correlate timestamps with Windows Event Viewer entries such as WHEA-Logger, Application Error, Display/nvlddmkm, Disk, Ntfs and stornvme.\n# Missing/null sensor values mean that the hardware/driver/API did not expose that sensor; do not treat null as zero.\n# This file is intentionally plain YAML so it can be pasted directly into an AI assistant for analysis.\n";
}

internal sealed class YamlSerializer
{
    private readonly ISerializer _serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public string Serialize(object value) => _serializer.Serialize(value);
}

internal sealed class GuiReport
{
    public string SchemaVersion { get; set; } = "0.2";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public HostInfo? Host { get; set; }
    public List<GuiSample> Samples { get; set; } = new();
    public List<MonitorError> Errors { get; set; } = new();
}

internal sealed class GuiSample
{
    public DateTimeOffset Timestamp { get; set; }
    public CpuMetrics? Cpu { get; set; }
    public MemoryMetrics? Memory { get; set; }
    public List<ProcessMetrics> TopProcesses { get; set; } = new();
    public NetworkMetrics? Network { get; set; }
    public DiskMetrics? Disk { get; set; }
    public List<GpuMetrics> Gpu { get; set; } = new();
}

internal sealed class MonitorError
{
    public DateTimeOffset Timestamp { get; set; }
    public string Message { get; set; } = "";
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();
}
