using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class TrafficAnalyzerService
{
    private static readonly JsonSerializerOptions _jsonUnsafe = AppJson.Api;

    private const string PatternsFileName = "traffic_patterns.json";
    private const int MinAnalysisIntervalMs = 30_000;
    private readonly LlmClient _llm;
    private readonly TrafficSnifferService _sniffer;
    private readonly IpRegistryService _ipRegistry;
    private readonly Action<string> _broadcast;
    private readonly string _patternsPath;
    private readonly object _lock = new();
    private List<TrafficPattern> _patterns = [];
    private int _isAnalyzing;
    private long _lastAnalysisTicks;
    private readonly ILogger<TrafficAnalyzerService>? _logger;

    public TrafficAnalyzerService(LlmClient llm, TrafficSnifferService sniffer,
        IpRegistryService ipRegistry, Action<string> broadcast, ILogger<TrafficAnalyzerService>? logger = null)
    {
        _llm = llm;
        _sniffer = sniffer;
        _ipRegistry = ipRegistry;
        _broadcast = broadcast;
        _patternsPath = ResolvePath(PatternsFileName);
        _logger = logger;
        LoadPatterns();
        _sniffer.OnBatchReady += OnBatchReady;
    }

    public IReadOnlyList<TrafficPattern> GetPatterns()
    {
        lock (_lock) return _patterns.ToList();
    }

    public void ClearPatterns()
    {
        lock (_lock)
        {
            _patterns.Clear();
            SavePatterns();
        }
    }

    private void OnBatchReady(int totalCaptured)
    {
        if (Interlocked.CompareExchange(ref _isAnalyzing, 1, 0) != 0) return;
        var now = DateTime.UtcNow.Ticks;
        var last = Volatile.Read(ref _lastAnalysisTicks);
        if (last != 0 && now - last < TimeSpan.FromMilliseconds(MinAnalysisIntervalMs).Ticks)
        {
            Interlocked.Exchange(ref _isAnalyzing, 0);
            return;
        }
        Volatile.Write(ref _lastAnalysisTicks, now);
        RunSafe.Run(ct2 => AnalyzeSafeAsync(totalCaptured), CancellationToken.None, "TrafficAnalyzer");
    }

    private async Task AnalyzeSafeAsync(int totalCaptured)
    {
        try
        {
            await AnalyzeAsync(totalCaptured);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ANALYZER] Error");
        }
        finally
        {
            Interlocked.Exchange(ref _isAnalyzing, 0);
        }
    }

    private async Task AnalyzeAsync(int totalCaptured)
    {
        var records = _sniffer.GetRecent(30);
        var ipMappings = _ipRegistry.GetAll();

        _logger?.LogInformation("[ANALYZER] Analyze: {RecordCount} records, {TotalCaptured} total, {IpCount} IPs known", records.Count, totalCaptured, ipMappings.Count);

        var prompt = BuildPrompt(records, ipMappings);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger?.LogInformation("[ANALYZER] Empty prompt (no relevant traffic)");
            return;
        }

        _logger?.LogInformation("[ANALYZER] Prompt built: {Length} chars", prompt.Length);

        var response = await _llm.GenerateAsync(prompt, new LlmRequestOptions(
            Temperature: 0.1, NumPredict: 512, Format: "json"));

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger?.LogInformation("[ANALYZER] No response from LLM");
            return;
        }

        _logger?.LogInformation("[ANALYZER] LLM response: {Response}", response[..Math.Min(response.Length, 200)]);
        ParseAndBroadcast(response);
    }

    private string BuildPrompt(IReadOnlyList<TrafficRecord> records, IReadOnlyList<IpMapping> ipMappings)
    {
        var recordsJson = JsonSerializer.Serialize(records.Select(r => new
        {
            time = r.Timestamp.ToString("HH:mm:ss"),
            src = r.SrcIp,
            dst = r.DstIp,
            proto = r.Protocol,
            sport = r.SrcPort,
            dport = r.DstPort,
            len = r.Length,
            msg = r.Summary
        }), _jsonUnsafe);

        var ipsJson = JsonSerializer.Serialize(ipMappings.Select(m => new
        {
            ip = m.Ip,
            name = m.Name
        }), _jsonUnsafe);

        var sb = new StringBuilder();
        sb.AppendLine("Ты — анализатор сетевого трафика для системы управления DMX/OSC оборудованием.");
        sb.AppendLine();
        sb.AppendLine("Дано:");
        sb.AppendLine("1) records — последние записи трафика (массив объектов: time, src, dst, proto, sport, dport, len, msg)");
        sb.AppendLine("2) ip_registry — известные IP-адреса с именами");
        sb.AppendLine();
        sb.AppendLine("Задача: найти паттерны в ArtNet и OSC трафике.");
        sb.AppendLine();
        sb.AppendLine("Формат ответа — JSON:");
        sb.AppendLine("{");
        sb.AppendLine("  \"patterns\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"description\": \"...\",");
        sb.AppendLine("      \"related_ips\": [\"...\"],");
        sb.AppendLine("      \"suggested_action\": \"group|macro|alert|info\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Правила для suggested_action:");
        sb.AppendLine("- \"group\" — несколько ArtNet-пакетов подряд на соседние каналы в одну вселенную (можно сгруппировать)");
        sb.AppendLine("- \"macro\" — OSC-команды, повторяющиеся циклически каждые N секунд, или одинаковые пути с разными значениями (можно сделать макрос)");
        sb.AppendLine("- \"alert\" — необычная активность: неизвестный IP шлёт много пакетов, нестандартные порты, подозрительные протоколы");
        sb.AppendLine("- \"info\" — одиночные команды, штатная работа, известные устройства");
        sb.AppendLine();
        sb.AppendLine("Примеры паттернов:");
        sb.AppendLine("- ArtNet: 3+ пакета DMX на каналы 1,2,3 за 1 секунду → \"group\"");
        sb.AppendLine("- ArtNet: периодический Poll каждые 5 секунд → \"macro\"");
        sb.AppendLine("- OSC: /avatar/parameters/FloatSlider = 0, 0.1, 0.2 → \"macro\"");
        sb.AppendLine("- OSC: /avatar/parameters/Blink каждые 2 секунды → \"macro\"");
        sb.AppendLine("- Неизвестный IP шлёт 20+ пакетов на ArtNet-порт → \"alert\"");
        sb.AppendLine();
        sb.AppendLine("Если records пусты — patterns: [].");
        sb.AppendLine();
        sb.AppendLine("records:");
        sb.AppendLine(recordsJson);
        sb.AppendLine();
        sb.AppendLine("ip_registry:");
        sb.AppendLine(ipsJson);
        return sb.ToString();
    }

    private void ParseAndBroadcast(string response)
    {
        try
        {
            var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var newPatterns = new List<TrafficPattern>();
            if (root.TryGetProperty("patterns", out var patternsEl) && patternsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in patternsEl.EnumerateArray())
                {
                    var desc = GetString(item, "description") ?? "Unknown pattern";
                    var action = GetString(item, "suggested_action") ?? "info";
                    var relatedIps = new List<string>();

                    if (item.TryGetProperty("related_ips", out var ips) && ips.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ip in ips.EnumerateArray())
                        {
                            if (ip.ValueKind == JsonValueKind.String)
                                relatedIps.Add(ip.GetString()!);
                        }
                    }

                    newPatterns.Add(new TrafficPattern(
                        $"pat_{Guid.NewGuid().ToString("N")[..8]}",
                        desc, relatedIps, action, DateTime.UtcNow));
                }
            }

            if (newPatterns.Count > 0)
            {
                lock (_lock)
                {
                    var existingDescs = new HashSet<string>(_patterns.Select(p => p.Description), StringComparer.OrdinalIgnoreCase);
                    var unique = newPatterns.Where(p => existingDescs.Add(p.Description)).ToList();
                    _patterns.AddRange(unique);
                    if (_patterns.Count > 200)
                        _patterns = _patterns.Skip(_patterns.Count - 200).ToList();
                    SavePatterns();
                }
            }

            var broadcastJson = JsonSerializer.Serialize(new
            {
                type = "traffic:analysis",
                patterns = newPatterns.Select(p => new
                {
                    id = p.Id,
                    description = p.Description,
                    related_ips = p.RelatedIps ?? [],
                    suggested_action = p.SuggestedAction,
                    detected_at = p.DetectedAt.ToString("O")
                })
            }, _jsonUnsafe);
            _broadcast(broadcastJson);

            _logger?.LogInformation("[ANALYZER] Found {Count} pattern(s)", newPatterns.Count);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "[ANALYZER] Parse error");
        }
    }

    private void LoadPatterns()
    {
        try
        {
            if (!File.Exists(_patternsPath)) { _patterns = []; return; }
            var json = File.ReadAllText(_patternsPath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("patterns", out var entries))
            {
                _patterns = JsonSerializer.Deserialize<List<TrafficPattern>>(entries.GetRawText(), AppJson.File) ?? [];
                _patterns = _patterns.Select(p => p with { RelatedIps = p.RelatedIps ?? [], SuggestedAction = p.SuggestedAction ?? "info" }).ToList();
            }
        }
        catch
        {
            _patterns = [];
        }
    }

    private void SavePatterns()
    {
        try
        {
            var json = JsonSerializer.Serialize(new { patterns = _patterns }, AppJson.File);
            File.WriteAllText(_patternsPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ANALYZER] Save patterns error");
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string ResolvePath(string fileName)
    {
        var dirs = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
        };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path)) return path;
        }
        return Path.Combine(Environment.CurrentDirectory, fileName);
    }
}
