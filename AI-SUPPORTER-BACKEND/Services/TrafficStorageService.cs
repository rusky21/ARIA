using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class TrafficStorageService : IAsyncDisposable
{
    private const int MaxAgeDays = 14;
    private const int BatchFlushIntervalMs = 1000;
    private const int BatchSize = 50;

    private SqliteConnection? _connection;
    private readonly ConcurrentQueue<TrafficRecord> _pendingQueue = new();
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private CancellationTokenSource? _flushCts;
    private Task? _flushTask;
    private readonly ILogger<TrafficStorageService>? _logger;

    private static string ResolveDbPath()
    {
        var dirs = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
        };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, "traffic.db");
            if (File.Exists(path)) return path;
        }
        return Path.Combine(Environment.CurrentDirectory, "traffic.db");
    }

    public TrafficStorageService(ILogger<TrafficStorageService>? logger = null)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var dbPath = ResolveDbPath();
        _logger?.LogInformation("[STORAGE] Database: {DbPath}", dbPath);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        await _connection.OpenAsync();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS traffic (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                ts       TEXT    NOT NULL,
                src_ip   TEXT    NOT NULL,
                dst_ip   TEXT    NOT NULL,
                protocol TEXT    NOT NULL,
                src_port INTEGER NOT NULL,
                dst_port INTEGER NOT NULL,
                length   INTEGER NOT NULL,
                summary  TEXT,
                ttl      INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_traffic_ts ON traffic(ts);
            CREATE INDEX IF NOT EXISTS idx_traffic_src ON traffic(src_ip);
            CREATE INDEX IF NOT EXISTS idx_traffic_dst ON traffic(dst_ip);
            CREATE INDEX IF NOT EXISTS idx_traffic_proto ON traffic(protocol);
            CREATE INDEX IF NOT EXISTS idx_traffic_src_dst ON traffic(src_ip, dst_ip);
        ";
        await cmd.ExecuteNonQueryAsync();

        _flushCts = new CancellationTokenSource();
        _flushTask = Task.Run(() => FlushLoop(_flushCts.Token));

        _logger?.LogInformation("[STORAGE] Initialized");
    }

    public void Enqueue(TrafficRecord record)
    {
        _pendingQueue.Enqueue(record);
    }

    private async Task FlushLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(BatchFlushIntervalMs, ct);
                await FlushBatchAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[STORAGE] Flush error");
            }
        }
        await FlushBatchAsync();
    }

    private async Task FlushBatchAsync()
    {
        if (_connection is null) return;

        await _dbLock.WaitAsync();
        try
        {
            var batch = new List<TrafficRecord>();
            while (batch.Count < BatchSize && _pendingQueue.TryDequeue(out var record))
                batch.Add(record);

            if (batch.Count == 0) return;

            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync();
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO traffic (ts, src_ip, dst_ip, protocol, src_port, dst_port, length, summary, ttl) " +
                              "VALUES ($ts, $src, $dst, $proto, $sp, $dp, $len, $sum, $ttl)";

            var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
            var pSrc = cmd.Parameters.Add("$src", SqliteType.Text);
            var pDst = cmd.Parameters.Add("$dst", SqliteType.Text);
            var pProto = cmd.Parameters.Add("$proto", SqliteType.Text);
            var pSp = cmd.Parameters.Add("$sp", SqliteType.Integer);
            var pDp = cmd.Parameters.Add("$dp", SqliteType.Integer);
            var pLen = cmd.Parameters.Add("$len", SqliteType.Integer);
            var pSum = cmd.Parameters.Add("$sum", SqliteType.Text);
            var pTtl = cmd.Parameters.Add("$ttl", SqliteType.Integer);

            foreach (var r in batch)
            {
                pTs.Value = r.Timestamp.ToString("O");
                pSrc.Value = r.SrcIp;
                pDst.Value = r.DstIp;
                pProto.Value = r.Protocol;
                pSp.Value = r.SrcPort;
                pDp.Value = r.DstPort;
                pLen.Value = r.Length;
                pSum.Value = (object?)r.Summary ?? DBNull.Value;
                pTtl.Value = r.Ttl;
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task CleanupOldRecordsAsync()
    {
        if (_connection is null) return;
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM traffic WHERE ts < datetime('now', '-{MaxAgeDays} days')";
            var deleted = await cmd.ExecuteNonQueryAsync();
            if (deleted > 0)
                _logger?.LogInformation("[STORAGE] Cleaned up {Deleted} old records", deleted);
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<TrafficRecord>> GetRecordsAsync(DateTime from, DateTime to)
    {
        if (_connection is null) return [];
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT ts, src_ip, dst_ip, protocol, src_port, dst_port, length, summary, ttl " +
                              "FROM traffic WHERE ts >= $from AND ts <= $to ORDER BY ts";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));

            var results = new List<TrafficRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new TrafficRecord(
                    DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    null,
                    (byte)reader.GetInt32(8)));
            }
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<int> GetCountAsync(DateTime from, DateTime to)
    {
        if (_connection is null) return 0;
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM traffic WHERE ts >= $from AND ts <= $to";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<FlowSummary>> GetTopFlowsAsync(DateTime from, DateTime to, int topN = 20)
    {
        if (_connection is null) return [];
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT src_ip, dst_ip, src_port, dst_port, protocol,
                       COUNT(*) as pkt_count, SUM(length) as total_bytes,
                       MIN(ts) as first_seen, MAX(ts) as last_seen
                FROM traffic
                WHERE ts >= $from AND ts <= $to
                GROUP BY src_ip, dst_ip, src_port, dst_port, protocol
                ORDER BY pkt_count DESC
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));
            cmd.Parameters.AddWithValue("$limit", topN);

            var results = new List<FlowSummary>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new FlowSummary(
                    reader.GetString(0), reader.GetString(1),
                    reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4),
                    reader.GetInt32(5), reader.GetInt64(6),
                    DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<ProtocolStat>> GetProtocolStatsAsync(DateTime from, DateTime to)
    {
        if (_connection is null) return [];
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT protocol, COUNT(*) as cnt, SUM(length) as total_bytes
                FROM traffic
                WHERE ts >= $from AND ts <= $to
                GROUP BY protocol
                ORDER BY cnt DESC";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));

            var results = new List<ProtocolStat>();
            long totalBytes = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ProtocolStat(
                    reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2), 0));
                totalBytes += reader.GetInt64(2);
            }

            if (totalBytes > 0)
                results = results.Select(p => new ProtocolStat(
                    p.Protocol, p.Count, p.TotalBytes,
                    Math.Round((double)p.TotalBytes / totalBytes * 100, 1))).ToList();

            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<HostStat>> GetTopHostsAsync(DateTime from, DateTime to, int topN = 20)
    {
        if (_connection is null) return [];
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                WITH sent AS (
                    SELECT src_ip as ip, COUNT(*) as cnt, SUM(length) as bytes,
                           COUNT(DISTINCT dst_ip) as dst_count
                    FROM traffic WHERE ts >= $from AND ts <= $to
                    GROUP BY src_ip
                ),
                received AS (
                    SELECT dst_ip as ip, COUNT(*) as cnt, SUM(length) as bytes,
                           COUNT(DISTINCT src_ip) as src_count
                    FROM traffic WHERE ts >= $from AND ts <= $to
                    GROUP BY dst_ip
                ),
                combined AS (
                    SELECT COALESCE(s.ip, r.ip) as ip,
                           COALESCE(s.cnt, 0) as sent_pkts, COALESCE(r.cnt, 0) as rcvd_pkts,
                           COALESCE(s.bytes, 0) as sent_bytes, COALESCE(r.bytes, 0) as rcvd_bytes,
                           COALESCE(s.dst_count, 0) as dst_count, COALESCE(r.src_count, 0) as src_count
                    FROM sent s LEFT JOIN received r ON s.ip = r.ip
                    UNION
                    SELECT COALESCE(r.ip, s.ip) as ip,
                           COALESCE(s.cnt, 0) as sent_pkts, COALESCE(r.cnt, 0) as rcvd_pkts,
                           COALESCE(s.bytes, 0) as sent_bytes, COALESCE(r.bytes, 0) as rcvd_bytes,
                           COALESCE(s.dst_count, 0) as dst_count, COALESCE(r.src_count, 0) as src_count
                    FROM received r LEFT JOIN sent s ON r.ip = s.ip
                )
                SELECT ip, sent_pkts, rcvd_pkts, sent_bytes, rcvd_bytes, dst_count, src_count
                FROM combined
                ORDER BY (sent_pkts + rcvd_pkts) DESC
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));
            cmd.Parameters.AddWithValue("$limit", topN);

            var results = new List<HostStat>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new HostStat(
                    reader.GetString(0), null,
                    reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt64(3), reader.GetInt64(4),
                    reader.GetInt32(5), reader.GetInt32(6)));
            }
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        DateTime from, DateTime to, int bucketMinutes)
    {
        if (_connection is null) return [];
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            var minuteStr = bucketMinutes < 10 ? $"0{bucketMinutes}" : bucketMinutes.ToString();
            cmd.CommandText =
                "SELECT strftime('%Y-%m-%dT%H:" + minuteStr + ":00Z', ts) as bucket, " +
                "COUNT(*) as pkt_count, SUM(length) as total_bytes " +
                "FROM traffic " +
                "WHERE ts >= $from AND ts <= $to " +
                "GROUP BY bucket ORDER BY bucket";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));

            var results = new List<TimeSeriesPoint>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new TimeSeriesPoint(
                    DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetInt32(1),
                    reader.GetInt64(2),
                    new Dictionary<string, int>()));
            }
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<AnomalyEvent>> DetectAnomaliesAsync(DateTime from, DateTime to)
    {
        if (_connection is null) return [];

        var records = await GetRecordsAsync(from, to);
        var anomalies = new List<AnomalyEvent>();
        if (records.Count == 0) return anomalies;

        var byMinute = records
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day,
                r.Timestamp.Hour, r.Timestamp.Minute, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .ToList();

        if (byMinute.Count < 2) return anomalies;

        var rates = byMinute.Select(g => (double)g.Count()).ToList();
        var mean = rates.Average();
        var stdDev = Math.Sqrt(rates.Average(r => Math.Pow(r - mean, 2)));
        var spikeThreshold = mean + 3 * stdDev;

        for (var i = 0; i < byMinute.Count; i++)
        {
            var count = byMinute[i].Count();
            if (count > spikeThreshold && count > mean * 2)
            {
                anomalies.Add(new AnomalyEvent(
                    $"anom_{Guid.NewGuid().ToString("N")[..8]}",
                    byMinute[i].Key, "spike",
                    $"Всплеск трафика: {count} пакетов/мин (норма: ~{mean:F0})",
                    null, 2));
            }
        }

        var bySrc = records.GroupBy(r => r.SrcIp);
        foreach (var srcGroup in bySrc)
        {
            var srcIp = srcGroup.Key;
            var dstPorts = srcGroup.Select(r => r.DstPort).Distinct().Count();
            if (dstPorts > 10 && srcGroup.Count() > 50)
            {
                anomalies.Add(new AnomalyEvent(
                    $"anom_{Guid.NewGuid().ToString("N")[..8]}",
                    srcGroup.First().Timestamp, "port_scan",
                    $"Обнаружен скан портов: {srcIp} → {dstPorts} различных портов ({srcGroup.Count()} пакетов)",
                    srcIp, 3));
            }
        }

        foreach (var srcGroup in bySrc)
        {
            var count = srcGroup.Count();
            var durationSec = (srcGroup.Max(r => r.Timestamp) - srcGroup.Min(r => r.Timestamp)).TotalSeconds;
            if (durationSec > 0 && count / durationSec > 1000)
            {
                anomalies.Add(new AnomalyEvent(
                    $"anom_{Guid.NewGuid().ToString("N")[..8]}",
                    srcGroup.First().Timestamp, "flood",
                    $"Флуд от {srcGroup.Key}: {count} пакетов за {durationSec:F0}с ({count / durationSec:F0} п/с)",
                    srcGroup.Key, 3));
            }
        }

        return anomalies.OrderByDescending(a => a.Severity).ThenByDescending(a => a.Timestamp).ToList();
    }

    public async Task<IReadOnlyList<CommandPattern>> DetectCommandPatternsAsync(DateTime from, DateTime to)
    {
        if (_connection is null) return [];

        var records = await GetRecordsAsync(from, to);
        var patterns = new List<CommandPattern>();
        if (records.Count < 5) return patterns;

        var grouped = records
            .GroupBy(r => $"{r.Protocol}:{r.SrcPort}->{r.DstPort}:{r.Length / 50}")
            .Where(g => g.Count() >= 3);

        foreach (var group in grouped)
        {
            var sorted = group.OrderBy(r => r.Timestamp).ToList();
            var intervals = new List<double>();
            for (var i = 1; i < sorted.Count; i++)
            {
                intervals.Add((sorted[i].Timestamp - sorted[i - 1].Timestamp).TotalMilliseconds);
            }

            if (intervals.Count == 0) continue;

            var meanInterval = intervals.Average();
            var stdDev = Math.Sqrt(intervals.Average(x => Math.Pow(x - meanInterval, 2)));
            var cv = meanInterval > 0 ? stdDev / meanInterval : 1.0;

            var relatedIps = sorted.Select(r => r.SrcIp).Distinct().ToList();
            var protocol = sorted[0].Protocol;
            var sig = sorted[0].Summary ?? $"{protocol}:{sorted[0].SrcPort}->{sorted[0].DstPort}";

            string description;
            if (cv < 0.3)
                description = $"Периодический: {sig} (каждые ~{meanInterval / 1000:F1}с)";
            else
                description = $"Повторяющийся: {sig} ({sorted.Count} раз, интервал ~{meanInterval / 1000:F1}с ± {stdDev / 1000:F1}с)";

            patterns.Add(new CommandPattern(
                $"{protocol}:{sorted[0].SrcPort}->{sorted[0].DstPort}:{sorted[0].Length}B",
                sorted.Count, meanInterval, description, relatedIps));
        }

        return patterns.OrderByDescending(p => p.Occurrences).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        _flushCts?.Cancel();
        if (_flushTask is not null)
        {
            try { await _flushTask; }
            catch (Exception ex) { _logger?.LogError(ex, "[STORAGE] Flush task error during dispose"); }
        }
        await FlushBatchAsync();
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        _flushCts?.Dispose();
    }
}
