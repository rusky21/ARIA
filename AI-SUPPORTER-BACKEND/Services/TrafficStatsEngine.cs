namespace AI_Supporter;

public static class TrafficStatsEngine
{
    public static IReadOnlyList<ProtocolStat> ProtocolBreakdown(IReadOnlyList<TrafficRecord> records)
    {
        if (records.Count == 0) return [];

        var totalBytes = records.Sum(r => (long)r.Length);
        var groups = records.GroupBy(r => r.Protocol)
            .Select(g => new ProtocolStat(
                g.Key,
                g.Count(),
                g.Sum(r => (long)r.Length),
                totalBytes > 0 ? Math.Round((double)g.Sum(r => r.Length) / totalBytes * 100, 1) : 0))
            .OrderByDescending(p => p.Count)
            .ToList();

        return groups;
    }

    public static IReadOnlyList<FlowSummary> TopFlows(IReadOnlyList<TrafficRecord> records, int topN = 20)
    {
        return records
            .GroupBy(r => $"{r.SrcIp}|{r.DstIp}|{r.SrcPort}|{r.DstPort}|{r.Protocol}")
            .Select(g => new FlowSummary(
                g.First().SrcIp, g.First().DstIp,
                g.First().SrcPort, g.First().DstPort, g.First().Protocol,
                g.Count(),
                g.Sum(r => (long)r.Length),
                g.Min(r => r.Timestamp),
                g.Max(r => r.Timestamp)))
            .OrderByDescending(f => f.PacketCount)
            .Take(topN)
            .ToList();
    }

    public static IReadOnlyList<HostStat> TopHosts(IReadOnlyList<TrafficRecord> records, int topN = 20)
    {
        var sent = records.GroupBy(r => r.SrcIp)
            .ToDictionary(g => g.Key, g => (
                sentPkts: g.Count(),
                sentBytes: g.Sum(r => (long)r.Length),
                dstCount: g.Select(r => r.DstIp).Distinct().Count()));

        var received = records.GroupBy(r => r.DstIp)
            .ToDictionary(g => g.Key, g => (
                rcvdPkts: g.Count(),
                rcvdBytes: g.Sum(r => (long)r.Length),
                srcCount: g.Select(r => r.SrcIp).Distinct().Count()));

        var allIps = sent.Keys.Union(received.Keys).Distinct();
        return allIps
            .Select(ip => new HostStat(
                ip, null,
                sent.TryGetValue(ip, out var s) ? s.sentPkts : 0,
                received.TryGetValue(ip, out var r) ? r.rcvdPkts : 0,
                s.sentBytes,
                r.rcvdBytes,
                s.dstCount,
                r.srcCount))
            .OrderByDescending(h => h.SentPackets + h.ReceivedPackets)
            .Take(topN)
            .ToList();
    }

    public static IReadOnlyList<TimeSeriesPoint> TimeSeries(
        IReadOnlyList<TrafficRecord> records, int bucketMinutes)
    {
        if (records.Count == 0) return [];

        var minTs = records.Min(r => r.Timestamp);
        var maxTs = records.Max(r => r.Timestamp);
        var bucketSpan = TimeSpan.FromMinutes(bucketMinutes);

        var buckets = new Dictionary<DateTime, List<TrafficRecord>>();
        var current = new DateTime(minTs.Year, minTs.Month, minTs.Day,
            minTs.Hour, minTs.Minute / bucketMinutes * bucketMinutes, 0, DateTimeKind.Utc);
        var end = maxTs + bucketSpan;

        while (current <= end)
        {
            buckets[current] = [];
            current += bucketSpan;
        }

        foreach (var record in records)
        {
            var bucketKey = new DateTime(
                record.Timestamp.Year, record.Timestamp.Month, record.Timestamp.Day,
                record.Timestamp.Hour, record.Timestamp.Minute / bucketMinutes * bucketMinutes,
                0, DateTimeKind.Utc);
            if (buckets.TryGetValue(bucketKey, out var list))
                list.Add(record);
        }

        return buckets
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new TimeSeriesPoint(
                kvp.Key,
                kvp.Value.Count,
                kvp.Value.Sum(r => (long)r.Length),
                kvp.Value.GroupBy(r => r.Protocol)
                    .ToDictionary(g => g.Key, g => g.Count())))
            .ToList();
    }

    public static IReadOnlyList<AnomalyEvent> DetectAnomalies(
        IReadOnlyList<TrafficRecord> records, IReadOnlyList<IpMapping> knownIps)
    {
        var anomalies = new List<AnomalyEvent>();
        if (records.Count < 10) return anomalies;

        var byMinute = records
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day,
                r.Timestamp.Hour, r.Timestamp.Minute, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .ToList();

        if (byMinute.Count >= 2)
        {
            var rates = byMinute.Select(g => (double)g.Count()).ToList();
            var mean = rates.Average();
            var stdDev = Math.Sqrt(rates.Average(r => Math.Pow(r - mean, 2)));
            var spikeThreshold = mean + 3 * stdDev;

            foreach (var bucket in byMinute)
            {
                var count = bucket.Count();
                if (count > spikeThreshold && count > mean * 2)
                {
                    anomalies.Add(new AnomalyEvent(
                        $"anom_{Guid.NewGuid().ToString("N")[..8]}",
                        bucket.Key, "spike",
                        $"Всплеск трафика: {count} пакетов/мин (норма: ~{mean:F0})",
                        null, 2));
                }
            }
        }

        var knownIpSet = new HashSet<string>(knownIps.Select(i => i.Ip), StringComparer.OrdinalIgnoreCase);

        var bySrc = records.GroupBy(r => r.SrcIp).ToList();
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
            var duration = (srcGroup.Max(r => r.Timestamp) - srcGroup.Min(r => r.Timestamp)).TotalSeconds;
            if (duration > 0 && count / duration > 1000)
            {
                anomalies.Add(new AnomalyEvent(
                    $"anom_{Guid.NewGuid().ToString("N")[..8]}",
                    srcGroup.First().Timestamp, "flood",
                    $"Флуд от {srcGroup.Key}: {count} пакетов за {duration:F0}с ({count / duration:F0} п/с)",
                    srcGroup.Key, 3));
            }
        }

        var unknownIps = records
            .Where(r => !knownIpSet.Contains(r.SrcIp))
            .GroupBy(r => r.SrcIp)
            .Where(g => g.Count() > 50);
        foreach (var group in unknownIps)
        {
            anomalies.Add(new AnomalyEvent(
                $"anom_{Guid.NewGuid().ToString("N")[..8]}",
                group.First().Timestamp, "new_device",
                $"Неизвестное устройство {group.Key}: {group.Count()} пакетов (не зарегистрировано)",
                group.Key, 1));
        }

        return anomalies.OrderByDescending(a => a.Severity).ThenByDescending(a => a.Timestamp).ToList();
    }

    public static IReadOnlyList<CommandPattern> DetectCommandPatterns(IReadOnlyList<TrafficRecord> records)
    {
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
            var sig = sorted[0].Summary ?? $"{sorted[0].Protocol}:{sorted[0].SrcPort}->{sorted[0].DstPort}";

            string description;
            if (cv < 0.3)
                description = $"Периодический: {sig} (каждые ~{meanInterval / 1000:F1}с)";
            else
                description = $"Повторяющийся: {sig} ({sorted.Count} раз, интервал ~{meanInterval / 1000:F1}с ± {stdDev / 1000:F1}с)";

            patterns.Add(new CommandPattern(
                $"{sorted[0].Protocol}:{sorted[0].SrcPort}->{sorted[0].DstPort}:{sorted[0].Length}B",
                sorted.Count, meanInterval, description, relatedIps));
        }

        return patterns.OrderByDescending(p => p.Occurrences).ToList();
    }

    public static PeriodComparison ComparePeriods(
        int currentPackets, int previousPackets,
        int currentFlows, int previousFlows)
    {
        var changePercent = previousPackets > 0
            ? Math.Round((double)(currentPackets - previousPackets) / previousPackets * 100, 1)
            : 0;

        return new PeriodComparison(
            currentPackets, previousPackets, changePercent,
            currentFlows, previousFlows);
    }
}
