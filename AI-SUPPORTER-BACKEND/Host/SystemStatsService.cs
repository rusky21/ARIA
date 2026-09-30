using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

/// <summary>
/// Collects system stats (CPU, RAM, GPU, processes) and pushes them to clients.
/// Cross-platform: uses platform-specific APIs on Windows, falls back to /proc on Linux.
/// </summary>
public sealed class SystemStatsService : IDisposable
{
    private readonly long _sysUptime = Environment.TickCount64;
    private readonly Dictionary<int, (DateTime time, TimeSpan total)> _prevCpu = new();
    private readonly TaskRegistryService _taskRegistry;
    private readonly WsEventBus _eventBus;
    private readonly GpuMonitor _gpuMonitor;
    private readonly ILogger<SystemStatsService>? _logger;

#if WINDOWS
    private System.Diagnostics.PerformanceCounter? _cpuCounter;
#endif

    public SystemStatsService(TaskRegistryService taskRegistry, WsEventBus eventBus, ILogger<SystemStatsService>? logger = null)
    {
        _taskRegistry = taskRegistry;
        _eventBus = eventBus;
        _logger = logger;
        _gpuMonitor = new GpuMonitor();

#if WINDOWS
        try
        {
            _cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _ = _cpuCounter.NextValue(); // seed first call
        }
        catch (Exception ex) { _logger?.LogError(ex, "[START] CPU counter unavailable"); }
#else
        _logger?.LogInformation("[START] Using cross-platform CPU stats (no PerformanceCounter)");
#endif

        try { _gpuMonitor.Initialize(); }
        catch (Exception ex) { _logger?.LogError(ex, "[START] GPU monitor unavailable"); }
    }

    public async Task CollectAndPushAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct);

                var sysCpu = GetSystemCpuPercent();
                var (totalMB, usedMB, availMB) = GetMemoryInfo();
                var ramPct = totalMB > 0 ? Math.Round((double)usedMB / totalMB * 100, 1) : 0.0;

                var uptimeSpan = TimeSpan.FromMilliseconds(Environment.TickCount64 - _sysUptime);
                var uptime = uptimeSpan.TotalHours > 1
                    ? $"{(int)uptimeSpan.TotalHours}ч {uptimeSpan.Minutes}м"
                    : $"{uptimeSpan.Minutes}м {uptimeSpan.Seconds}с";

                var (gpuTotal, gpuPerPid) = _gpuMonitor.ReadEngine();
                var (vramUsedMB, vramLimitMB, _) = _gpuMonitor.ReadAdapterMemory();

                var now = DateTime.UtcNow;
                var all = Process.GetProcesses();
                var snap = new List<(int pid, string name, double cpu, double ramPct, string mem, long ws, double gpu)>();
                foreach (var p in all)
                {
                    try
                    {
                        var pid = p.Id;
                        var name = p.ProcessName;
                        var ws = p.WorkingSet64 / 1024 / 1024;
                        var ramProcPct = totalMB > 0 ? Math.Round((double)ws / totalMB * 100, 1) : 0.0;
                        var gpuPct = gpuPerPid.TryGetValue(pid, out var gv) ? Math.Round(Math.Min(gv, 100), 1) : 0.0;

                        try
                        {
                            var tp = p.TotalProcessorTime;

                            if (_prevCpu.TryGetValue(pid, out var prev))
                            {
                                var elapsed = (now - prev.time).TotalMilliseconds;
                                var cpuDelta = (tp - prev.total).TotalMilliseconds;
                                var pct = elapsed > 0
                                    ? Math.Round(cpuDelta / elapsed / Environment.ProcessorCount * 100, 1)
                                    : 0.0;
                                snap.Add((pid, name, pct, ramProcPct, $"{ws} МБ", ws, gpuPct));
                            }
                            else
                            {
                                snap.Add((pid, name, 0.0, ramProcPct, $"{ws} МБ", ws, gpuPct));
                            }

                            _prevCpu[pid] = (now, tp);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // TotalProcessorTime not supported on some platforms — report 0 CPU
                            snap.Add((pid, name, 0.0, ramProcPct, $"{ws} МБ", ws, gpuPct));
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[SYSTEM] Process stats error for PID {p.Id}: {ex.Message}"); }
                    finally { p.Dispose(); }
                }

                var stale = _prevCpu.Keys.Where(id => snap.All(s => s.pid != id)).ToArray();
                foreach (var id in stale) _prevCpu.Remove(id);

                var top = snap.OrderByDescending(s => s.cpu).Take(300).ToArray();
                var procDtos = top.Select(s => new
                {
                    pid = s.pid,
                    name = s.name,
                    cpu = s.cpu,
                    ram = s.ramPct,
                    mem = s.mem,
                    gpu = s.gpu
                }).ToArray();

                var selfStat = snap.FirstOrDefault(s => s.pid == Environment.ProcessId);

                _eventBus.SystemStats(sysCpu, ramPct, totalMB, usedMB, availMB,
                    gpuTotal, vramUsedMB, vramLimitMB, uptime, procDtos,
                    _taskRegistry.GetLoads(), selfStat.ramPct, selfStat.ws, selfStat.gpu);
            }
            catch (Exception ex) { Debug.WriteLine($"[SYSTEM] Stats collection error: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
#if WINDOWS
        _cpuCounter?.Dispose();
#endif
        _gpuMonitor.Dispose();
    }

    private double GetSystemCpuPercent()
    {
#if WINDOWS
        try { if (_cpuCounter is not null) return Math.Round(_cpuCounter.NextValue(), 1); }
        catch (Exception ex) { Debug.WriteLine($"[SYSTEM] CPU counter error: {ex.Message}"); }
#endif
        return 0.0;
    }

    private static (long totalMB, long usedMB, long availMB) GetMemoryInfo()
    {
        if (OperatingSystem.IsWindows())
            return GetMemoryInfoWindows();
        if (OperatingSystem.IsLinux())
            return GetMemoryInfoLinux();
        if (OperatingSystem.IsMacOS())
            return GetMemoryInfoMacOs();
        return (0, 0, 0);
    }

    private static (long totalMB, long usedMB, long availMB) GetMemoryInfoWindows()
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                var totalMB = (long)(mem.ullTotalPhys / 1024 / 1024);
                var availMB = (long)(mem.ullAvailPhys / 1024 / 1024);
                return (totalMB, totalMB - availMB, availMB);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    private static (long totalMB, long usedMB, long availMB) GetMemoryInfoLinux()
    {
        try
        {
            long totalKB = 0, availKB = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:"))
                    totalKB = ParseMemInfoValue(line);
                else if (line.StartsWith("MemAvailable:"))
                    availKB = ParseMemInfoValue(line);

                if (totalKB > 0 && availKB > 0) break;
            }
            if (totalKB > 0)
            {
                var totalMB = totalKB / 1024;
                var availMB = availKB / 1024;
                return (totalMB, totalMB - availMB, availMB);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    private static long ParseMemInfoValue(string line)
    {
        var parts = line.Split(':', 2);
        if (parts.Length < 2) return 0;
        var numStr = parts[1].Trim().Split(' ')[0];
        return long.TryParse(numStr, out var val) ? val : 0;
    }

    private static (long totalMB, long usedMB, long availMB) GetMemoryInfoMacOs()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sysctl",
                Arguments = "hw.memsize",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(1000);
                if (output.StartsWith("hw.memsize:") && long.TryParse(output.Split(':')[1].Trim(), out var bytes))
                {
                    var totalMB = bytes / 1024 / 1024;
                    var usedMB = totalMB / 4; // rough estimate
                    return (totalMB, usedMB, totalMB - usedMB);
                }
            }
        }
        catch { }
        return (0, 0, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

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
