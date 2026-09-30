using System.Text.RegularExpressions;

namespace AI_Supporter;

public sealed class GpuMonitor : IDisposable
{
#if WINDOWS
    private const string EngineCategory = "GPU Engine";
    private const string AdapterCategory = "GPU Adapter Memory";
    private static readonly Regex PidRegex = new(@"pid_(\d+)", RegexOptions.Compiled);

    private readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _engine = new();
    private System.Diagnostics.PerformanceCounter? _adapterUsage;
    private System.Diagnostics.PerformanceCounter? _adapterLimit;
    private readonly object _lock = new();
#endif

    public bool Available { get; private set; }

    public void Initialize()
    {
#if WINDOWS
        lock (_lock)
        {
            try
            {
                Available = System.Diagnostics.PerformanceCounterCategory.Exists(EngineCategory)
                            || System.Diagnostics.PerformanceCounterCategory.Exists(AdapterCategory);
            }
            catch
            {
                Available = false;
            }
        }
#else
        Available = false;
#endif
    }

    public (double totalPct, Dictionary<int, double> perPid) ReadEngine()
    {
#if WINDOWS
        lock (_lock)
        {
            var perPid = new Dictionary<int, double>();
            double total = 0;

            try
            {
                if (!System.Diagnostics.PerformanceCounterCategory.Exists(EngineCategory))
                {
                    ReleaseEngine();
                    return (0, perPid);
                }

                var category = new System.Diagnostics.PerformanceCounterCategory(EngineCategory);
                var instances = category.GetInstanceNames();
                foreach (var instance in instances)
                {
                    if (!_engine.TryGetValue(instance, out var pc))
                    {
                        pc = new System.Diagnostics.PerformanceCounter(EngineCategory, "Utilization Percentage", instance, true);
                        _ = pc.NextValue(); // seed: first call returns 0
                        _engine[instance] = pc;
                    }

                    var val = pc.NextValue();
                    if (val < 0) val = 0;
                    total += val;

                    var match = PidRegex.Match(instance);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
                        perPid[pid] = perPid.GetValueOrDefault(pid) + val;
                }

                var stale = _engine.Keys.Where(k => !instances.Contains(k)).ToArray();
                foreach (var key in stale)
                {
                    _engine[key].Dispose();
                    _engine.Remove(key);
                }
            }
            catch
            {
                // GPU counters unavailable (no WDDM GPU, restricted system)
            }

            return (Math.Round(Math.Min(total, 100), 1), perPid);
        }
#else
        return (0, new Dictionary<int, double>());
#endif
    }

    public (long usedMB, long limitMB, double pct) ReadAdapterMemory()
    {
#if WINDOWS
        lock (_lock)
        {
            double used = 0, limit = 0;

            try
            {
                if (!System.Diagnostics.PerformanceCounterCategory.Exists(AdapterCategory))
                {
                    ReleaseAdapter();
                    return (0, 0, 0);
                }

                if (_adapterUsage is null || _adapterLimit is null)
                {
                    var category = new System.Diagnostics.PerformanceCounterCategory(AdapterCategory);
                    var instances = category.GetInstanceNames();
                    var instance = instances.Contains("_Total")
                        ? "_Total"
                        : instances.FirstOrDefault() ?? "";
                    if (instance.Length == 0)
                        return (0, 0, 0);

                    _adapterUsage = new System.Diagnostics.PerformanceCounter(AdapterCategory, "Dedicated Usage", instance, true);
                    _ = _adapterUsage.NextValue();
                    _adapterLimit = new System.Diagnostics.PerformanceCounter(AdapterCategory, "Dedicated Limit", instance, true);
                    _ = _adapterLimit.NextValue();
                }

                used = _adapterUsage.NextValue();
                limit = _adapterLimit.NextValue();
            }
            catch
            {
                ReleaseAdapter();
                return (0, 0, 0);
            }

            var usedMB = (long)(used / 1024 / 1024);
            var limitMB = (long)(limit / 1024 / 1024);
            var pct = limitMB > 0 ? Math.Round((double)usedMB / limitMB * 100, 1) : 0.0;
            return (usedMB, limitMB, pct);
        }
#else
        return (0, 0, 0);
#endif
    }

#if WINDOWS
    private void ReleaseEngine()
    {
        foreach (var pc in _engine.Values) pc.Dispose();
        _engine.Clear();
    }

    private void ReleaseAdapter()
    {
        _adapterUsage?.Dispose();
        _adapterLimit?.Dispose();
        _adapterUsage = null;
        _adapterLimit = null;
    }
#endif

    public void Dispose()
    {
#if WINDOWS
        lock (_lock)
        {
            ReleaseEngine();
            ReleaseAdapter();
        }
#endif
    }
}
