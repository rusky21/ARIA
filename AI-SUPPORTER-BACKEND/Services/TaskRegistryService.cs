using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed record TaskInfo(
    string Id,
    string Name,
    string Schedule,
    string Description,
    string Icon,
    bool Enabled,
    DateTime? LastRun,
    DateTime? NextRun,
    string? LastError);

public sealed record TaskLoadInfo(
    string Id,
    string Name,
    string Icon,
    bool Enabled,
    bool Continuous,
    double CpuPct,
    bool InFlight,
    DateTime? LastRun,
    DateTime? NextRun,
    string? LastError);

public sealed class TaskRegistryService : IDisposable
{
    private sealed class RegisteredTask
    {
        public string Id = "";
        public string Name = "";
        public string Schedule = "";
        public string Description = "";
        public string Icon = "";
        public bool Enabled;
        public DateTime? LastRun;
        public DateTime? NextRun;
        public string? LastError;
        public int IntervalSec = 60;
        public bool InFlight;
        public bool Continuous;
        public DateTime? ActiveSince;
        public Func<Task> Action = () => Task.CompletedTask;
        public Func<Task>? DisableAction;
    }

    private const int LoopIntervalSec = 10;
    private const int SampleIntervalMs = 500;
    private const int RingSize = 30;
    private readonly List<RegisteredTask> _registry = new();
    private readonly WsEventBus _eventBus;
    private readonly ILogger<TaskRegistryService>? _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private readonly List<CpuTick> _cpuRing = new();
    private Process? _selfProcess;

    private readonly struct CpuTick(DateTime time, double cpuMs, IReadOnlyList<string> activeIds)
    {
        public DateTime Time { get; } = time;
        public double CpuMs { get; } = cpuMs;
        public IReadOnlyList<string> ActiveIds { get; } = activeIds;
    }

    public TaskRegistryService(
        TrafficSnifferService sniffer,
        ArtnetService artnet,
        OscServerService oscServer,
        IpRegistryService ipRegistry,
        WsEventBus eventBus,
        ILogger<TaskRegistryService>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
        Register("traffic_capture", "Захват трафика", "постоянно",
            "Фиксирует весь сетевой трафик через raw-socket (требует прав администратора).",
            "network", enabled: true, intervalSec: 60, continuous: true,
            action: () =>
            {
                sniffer.Start();
                return Task.CompletedTask;
            },
            disable: () =>
            {
                sniffer.Stop();
                return Task.CompletedTask;
            });

        Register("osc_server", "OSC-сервер", "постоянно",
            "Принимает OSC-сообщения на UDP-порту 8000 и отвечает эхом.",
            "audio", enabled: true, intervalSec: 60, continuous: true,
            action: () =>
            {
                oscServer.Start();
                return Task.CompletedTask;
            },
            disable: () =>
            {
                oscServer.Stop();
                return Task.CompletedTask;
            });

        Register("artnet_poll", "ArtNet-скан", "каждые 60 с",
            "Периодически опрашивает ArtNet-устройства и обновляет список на вкладке «Тест».",
            "wifi", enabled: false, intervalSec: 60,
            action: async () =>
            {
                var ips = await artnet.SendPollAsync(sniffer);
                var devices = ips.Count > 0
                    ? ips.Select(ip => (ip, ipRegistry.Lookup(ip) ?? "Unknown Device")).ToList()
                    : TestDataService.GetSimulatedDevices(ipRegistry);
                eventBus.ArtPollResult(devices);
            });

        Register("vuln_check", "Проверка уязвимостей", "каждые 30 мин",
            "Проверяет базовые настройки безопасности (пароль, JWT-ключ, порты).",
            "shield", enabled: true, intervalSec: 1800,
            action: () =>
            {
                RunVulnerabilityCheck(eventBus);
                return Task.CompletedTask;
            });
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _selfProcess = Process.GetCurrentProcess();
        var ct = _cts.Token;
        RunSafe.Run(c => LoopAsync(c), ct, "TaskRegistry.Loop");
        RunSafe.Run(c => SampleLoopAsync(c), ct, "TaskRegistry.Sampler");

        var vuln = _registry.FirstOrDefault(t => t.Id == "vuln_check");
        if (vuln is not null)
        {
            lock (_lock)
            {
                vuln.InFlight = true;
                vuln.NextRun = DateTime.UtcNow.AddSeconds(vuln.IntervalSec);
            }
            RunSafe.Run(() => RunTaskAsync(vuln), "TaskRegistry.VulnCheck");
        }
    }

    public IReadOnlyList<TaskInfo> GetAll()
    {
        lock (_lock)
        {
            return _registry.Select(t => new TaskInfo(
                t.Id, t.Name, t.Schedule, t.Description, t.Icon,
                t.Enabled, t.LastRun, t.NextRun, t.LastError)).ToArray();
        }
    }

    public bool Toggle(string id)
    {
        Func<Task>? disable = null;
        lock (_lock)
        {
            var task = _registry.FirstOrDefault(t => t.Id == id);
            if (task is null) return false;

            task.Enabled = !task.Enabled;
            if (task.Enabled)
            {
                task.NextRun = DateTime.UtcNow.AddSeconds(Math.Min(LoopIntervalSec, task.IntervalSec));
                if (task.Continuous)
                    task.ActiveSince = DateTime.UtcNow;
            }
            else
            {
                task.NextRun = null;
                task.ActiveSince = null;
                disable = task.DisableAction;
            }
        }

        if (disable is not null)
            RunSafe.Run(() => disable(), $"TaskRegistry.Disable.{id}");
        return true;
    }

    public bool RunNow(string id)
    {
        lock (_lock)
        {
            var task = _registry.FirstOrDefault(t => t.Id == id);
            if (task is null || task.InFlight) return false;
            task.NextRun = DateTime.UtcNow;
            return true;
        }
    }

    private void Register(
        string id, string name, string schedule, string description, string icon,
        bool enabled, int intervalSec, Func<Task> action, Func<Task>? disable = null,
        bool continuous = false)
    {
        lock (_lock)
        {
            _registry.Add(new RegisteredTask
            {
                Id = id,
                Name = name,
                Schedule = schedule,
                Description = description,
                Icon = icon,
                Enabled = enabled,
                IntervalSec = intervalSec,
                Action = action,
                DisableAction = disable,
                Continuous = continuous,
                ActiveSince = enabled && continuous ? DateTime.UtcNow : null,
                NextRun = enabled ? DateTime.UtcNow.AddSeconds(Math.Min(LoopIntervalSec, intervalSec)) : null,
            });
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(LoopIntervalSec), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            RegisteredTask[] snapshot;
            lock (_lock) snapshot = _registry.ToArray();

            var now = DateTime.UtcNow;
            foreach (var task in snapshot)
            {
                lock (_lock)
                {
                    if (!task.Enabled || task.InFlight) continue;
                    if (task.NextRun is not null && task.NextRun > now) continue;
                    task.InFlight = true;
                    task.NextRun = now.AddSeconds(task.IntervalSec);
                }

                RunSafe.Run(() => RunTaskAsync(task), $"TaskRegistry.{task.Id}");
            }
        }
    }

    private async Task RunTaskAsync(RegisteredTask task)
    {
        lock (_lock)
        {
            if (!task.Continuous)
                task.ActiveSince = DateTime.UtcNow;
        }

        try
        {
            await task.Action();
            lock (_lock)
            {
                task.LastRun = DateTime.UtcNow;
                task.LastError = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TASK] {TaskId}: execution failed", task.Id);
            lock (_lock) task.LastError = ex.Message;
        }
        finally
        {
            lock (_lock)
            {
                task.InFlight = false;
                if (!task.Continuous)
                    task.ActiveSince = null;
            }
        }

        _eventBus.Tasks(GetAll());
    }

    private async Task SampleLoopAsync(CancellationToken ct)
    {
        var lastWall = DateTime.UtcNow;
        var lastCpu = _selfProcess?.TotalProcessorTime ?? TimeSpan.Zero;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SampleIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var nowWall = DateTime.UtcNow;
                var nowCpu = _selfProcess?.TotalProcessorTime ?? TimeSpan.Zero;
                var cpuDeltaMs = Math.Max(0, (nowCpu - lastCpu).TotalMilliseconds);
                lastWall = nowWall;
                lastCpu = nowCpu;

                string[] active;
                lock (_lock)
                {
                    active = _registry
                        .Where(t => t.Enabled && t.ActiveSince is not null)
                        .Select(t => t.Id)
                        .ToArray();

                    _cpuRing.Add(new CpuTick(nowWall, cpuDeltaMs, active));
                    if (_cpuRing.Count > RingSize)
                        _cpuRing.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[TASK] Sampler error");
            }
        }
    }

    public IReadOnlyList<TaskLoadInfo> GetLoads()
    {
        CpuTick[] ring;
        RegisteredTask[] tasks;
        lock (_lock)
        {
            ring = _cpuRing.ToArray();
            tasks = _registry.ToArray();
        }

        var windowMs = ring.Length > 1
            ? (ring[^1].Time - ring[0].Time).TotalMilliseconds
            : 0.0;
        if (windowMs <= 0)
        {
            return tasks.Select(t => new TaskLoadInfo(
                t.Id, t.Name, t.Icon, t.Enabled, t.Continuous, 0.0, t.InFlight, t.LastRun, t.NextRun, t.LastError)).ToArray();
        }

        return tasks.Select(t =>
        {
            double cpuMs = 0;
            foreach (var tick in ring)
            {
                if (tick.ActiveIds.Count == 0) continue;
                if (tick.ActiveIds.Contains(t.Id))
                    cpuMs += tick.CpuMs / tick.ActiveIds.Count;
            }
            var pct = Math.Round(cpuMs / windowMs * 100, 1);
            return new TaskLoadInfo(t.Id, t.Name, t.Icon, t.Enabled, t.Continuous, pct, t.InFlight, t.LastRun, t.NextRun, t.LastError);
        }).ToArray();
    }

    private static void RunVulnerabilityCheck(WsEventBus eventBus)
    {
        var issues = new List<string>();

        if ((Environment.GetEnvironmentVariable("AI_SUPPORTER_ADMIN_PASS") ?? "admin") == "admin")
            issues.Add("Используется стандартный пароль администратора (admin)");
        if ((Environment.GetEnvironmentVariable("AI_SUPPORTER_JWT_KEY") ?? "dev-secret-key-change-in-production-min-32-chars!!")
                == "dev-secret-key-change-in-production-min-32-chars!!")
            issues.Add("Используется JWT-ключ по умолчанию");
        if (Environment.GetEnvironmentVariable("AI_SUPPORTER_HTTP_PORT") is null)
            issues.Add("HTTP-порт не задан, используется стандартный 5000");
        if (!bool.TryParse(Environment.GetEnvironmentVariable("AI_SUPPORTER_HTTPS"), out var https) || !https)
            issues.Add("TLS отключён (AI_SUPPORTER_HTTPS не задан)");

        VulnerabilityCache.HasVulnerabilities = issues.Count > 0;
        VulnerabilityCache.Details = issues.Count > 0 ? string.Join("; ", issues) : null;
        VulnerabilityCache.LastCheck = DateTime.UtcNow;

        if (issues.Count > 0)
            eventBus.Popup("Проверка уязвимостей", "Найдены проблемы:\n" + string.Join("\n• ", issues));
        else
            Console.WriteLine("[TASK] vuln_check: проблем не найдено");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _selfProcess?.Dispose();
        _selfProcess = null;
    }
}
