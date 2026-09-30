using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class TrafficReportService
{
    private static readonly JsonSerializerOptions _jsonUnsafe = AppJson.File;

    private readonly TrafficStorageService _storage;
    private readonly TrafficSnifferService _sniffer;
    private readonly IpRegistryService _ipRegistry;
    private readonly LlmClient _llm;
    private readonly ILogger<TrafficReportService>? _logger;

    public TrafficReportService(TrafficStorageService storage, TrafficSnifferService sniffer,
        IpRegistryService ipRegistry, LlmClient llm, ILogger<TrafficReportService>? logger = null)
    {
        _storage = storage;
        _sniffer = sniffer;
        _ipRegistry = ipRegistry;
        _llm = llm;
        _logger = logger;
    }

    public async Task<TrafficReport> GenerateInstantReportAsync()
    {
        _logger?.LogInformation("[REPORT] Generating instant report...");
        var now = DateTime.UtcNow;
        var from = now.AddMinutes(-5);
        var recent = _sniffer.GetRecent(1000);
        var dbRecords = await _storage.GetRecordsAsync(from, now);
        var allRecords = recent.Concat(dbRecords).GroupBy(r =>
            $"{r.Timestamp.Ticks}_{r.SrcIp}_{r.DstIp}_{r.SrcPort}_{r.DstPort}_{r.Length}")
            .Select(g => g.First()).OrderBy(r => r.Timestamp).ToList();

        var protocols = TrafficStatsEngine.ProtocolBreakdown(allRecords);
        var flows = TrafficStatsEngine.TopFlows(allRecords, 10);
        var hosts = TrafficStatsEngine.TopHosts(allRecords, 10);
        var timeSeries = TrafficStatsEngine.TimeSeries(allRecords, 1);
        var knownIps = _ipRegistry.GetAll();
        var anomalies = TrafficStatsEngine.DetectAnomalies(allRecords, knownIps);
        var commandPatterns = TrafficStatsEngine.DetectCommandPatterns(allRecords);

        var totalBytes = allRecords.Sum(r => (long)r.Length);
        _logger?.LogInformation("[REPORT] Instant: {RecordCount} records, {ProtocolCount} protocols, {AnomalyCount} anomalies", allRecords.Count, protocols.Count, anomalies.Count);

        return new TrafficReport(
            "instant", now, from, now,
            allRecords.Count, totalBytes,
            protocols, flows, hosts, timeSeries, anomalies, commandPatterns,
            null, null);
    }

    public async Task<TrafficReport> GenerateDailyReportAsync()
    {
        _logger?.LogInformation("[REPORT] Generating daily report...");
        var now = DateTime.UtcNow;
        var from = now.AddHours(-24);
        var prevFrom = from.AddHours(-24);
        var prevTo = from;

        var records = await _storage.GetRecordsAsync(from, now);
        var prevRecords = await _storage.GetRecordsAsync(prevFrom, prevTo);

        var protocols = TrafficStatsEngine.ProtocolBreakdown(records);
        var flows = TrafficStatsEngine.TopFlows(records, 20);
        var hosts = TrafficStatsEngine.TopHosts(records, 20);
        var timeSeries = TrafficStatsEngine.TimeSeries(records, 15);
        var knownIps = _ipRegistry.GetAll();
        var anomalies = TrafficStatsEngine.DetectAnomalies(records, knownIps);
        var commandPatterns = TrafficStatsEngine.DetectCommandPatterns(records);
        var comparison = TrafficStatsEngine.ComparePeriods(
            records.Count, prevRecords.Count,
            TrafficStatsEngine.TopFlows(records, 9999).Count,
            TrafficStatsEngine.TopFlows(prevRecords, 9999).Count);

        var totalBytes = records.Sum(r => (long)r.Length);
        _logger?.LogInformation("[REPORT] Daily: {RecordCount} records, {ChangePercent}% vs previous", records.Count, comparison.ChangePercent);

        var llmSummary = await GenerateLlmSummaryAsync("сутки", protocols, flows, hosts,
            anomalies, commandPatterns, comparison, knownIps);

        return new TrafficReport(
            "daily", now, from, now,
            records.Count, totalBytes,
            protocols, flows, hosts, timeSeries, anomalies, commandPatterns,
            comparison, llmSummary);
    }

    public async Task<TrafficReport> GenerateWeeklyReportAsync()
    {
        _logger?.LogInformation("[REPORT] Generating weekly report...");
        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);
        var prevFrom = from.AddDays(-7);
        var prevTo = from;

        var records = await _storage.GetRecordsAsync(from, now);
        var prevRecords = await _storage.GetRecordsAsync(prevFrom, prevTo);

        var protocols = TrafficStatsEngine.ProtocolBreakdown(records);
        var flows = TrafficStatsEngine.TopFlows(records, 50);
        var hosts = TrafficStatsEngine.TopHosts(records, 50);
        var timeSeries = TrafficStatsEngine.TimeSeries(records, 60);
        var knownIps = _ipRegistry.GetAll();
        var anomalies = TrafficStatsEngine.DetectAnomalies(records, knownIps);
        var commandPatterns = TrafficStatsEngine.DetectCommandPatterns(records);
        var comparison = TrafficStatsEngine.ComparePeriods(
            records.Count, prevRecords.Count,
            TrafficStatsEngine.TopFlows(records, 9999).Count,
            TrafficStatsEngine.TopFlows(prevRecords, 9999).Count);

        var totalBytes = records.Sum(r => (long)r.Length);
        _logger?.LogInformation("[REPORT] Weekly: {RecordCount} records, {ChangePercent}% vs previous", records.Count, comparison.ChangePercent);

        var llmSummary = await GenerateLlmSummaryAsync("неделю", protocols, flows, hosts,
            anomalies, commandPatterns, comparison, knownIps);

        return new TrafficReport(
            "weekly", now, from, now,
            records.Count, totalBytes,
            protocols, flows, hosts, timeSeries, anomalies, commandPatterns,
            comparison, llmSummary);
    }

    private async Task<string?> GenerateLlmSummaryAsync(
        string periodLabel,
        IReadOnlyList<ProtocolStat> protocols,
        IReadOnlyList<FlowSummary> flows,
        IReadOnlyList<HostStat> hosts,
        IReadOnlyList<AnomalyEvent> anomalies,
        IReadOnlyList<CommandPattern> patterns,
        PeriodComparison comparison,
        IReadOnlyList<IpMapping> knownIps)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ты — эксперт по сетевому трафику DMX/OSC систем.");
            sb.AppendLine($"Составь подробный анализ трафика за {periodLabel} на русском языке.");
            sb.AppendLine();

            sb.AppendLine("Статистика по протоколам:");
            foreach (var p in protocols.Take(10))
                sb.AppendLine($"  {p.Protocol}: {p.Count} пакетов ({p.TotalBytes} байт, {p.Percentage}%)");

            sb.AppendLine();
            sb.AppendLine("Топ-потоки (src→dst):");
            foreach (var f in flows.Take(10))
            {
                var srcName = knownIps.FirstOrDefault(i => i.Ip == f.SrcIp)?.Name;
                var dstName = knownIps.FirstOrDefault(i => i.Ip == f.DstIp)?.Name;
                sb.AppendLine($"  {f.SrcIp}({srcName ?? "?"}) → {f.DstIp}({dstName ?? "?"}) [{f.Protocol}]: {f.PacketCount} пакетов, {f.TotalBytes} байт");
            }

            sb.AppendLine();
            sb.AppendLine("Топ-хосты:");
            foreach (var h in hosts.Take(10))
            {
                var name = knownIps.FirstOrDefault(i => i.Ip == h.Ip)?.Name;
                sb.AppendLine($"  {h.Ip}({name ?? "?"}): отправил {h.SentPackets}, получил {h.ReceivedPackets}");
            }

            if (anomalies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Аномалии:");
                foreach (var a in anomalies.Take(15))
                    sb.AppendLine($"  [{a.Type}] {a.Description}");
            }

            if (patterns.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Паттерны команд:");
                foreach (var p in patterns.Take(15))
                    sb.AppendLine($"  {p.Description} ({p.Occurrences} раз, IP: {string.Join(", ", p.RelatedIps)})");
            }

            sb.AppendLine();
            sb.AppendLine($"Сравнение: текущий период {comparison.CurrentPackets} пакетов, " +
                         $"прошлый {comparison.PreviousPackets} пакетов ({comparison.ChangePercent:+0.0;-0.0}%)");

            sb.AppendLine();
            sb.AppendLine("Составь анализ:");
            sb.AppendLine("1. Общая сводка трафика");
            sb.AppendLine("2. Наиболее активные устройства и их роль");
            sb.AppendLine("3. Выявленные паттерны команд и их значение");
            sb.AppendLine("4. Аномалии и их возможные причины");
            sb.AppendLine("5. Сравнение с предыдущим периодом");
            sb.AppendLine("6. Рекомендации по оптимизации и безопасности");

            var response = await _llm.GenerateAsync(sb.ToString(), new LlmRequestOptions(
                Temperature: 0.3, NumPredict: 1024));

            _logger?.LogInformation("[REPORT] LLM summary: {Length} chars", response?.Length ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[REPORT] LLM summary error");
            return null;
        }
    }
}
