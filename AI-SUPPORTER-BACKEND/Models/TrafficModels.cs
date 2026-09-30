namespace AI_Supporter;

public sealed record TrafficRecord(
    DateTime Timestamp,
    string SrcIp,
    string DstIp,
    string Protocol,
    int SrcPort,
    int DstPort,
    int Length,
    string? Summary,
    byte[]? Raw = null,
    byte Ttl = 0,
    ulong Seq = 0);

public sealed record IpMapping(
    string Ip,
    string Name,
    string? Notes,
    DateTime FirstSeen);

public sealed record TrafficPattern(
    string Id,
    string Description,
    IReadOnlyList<string>? RelatedIps,
    string SuggestedAction = "info",
    DateTime DetectedAt = default);



public sealed record TrafficStats(
    int TotalPackets,
    IReadOnlyDictionary<string, int> Protocols,
    string? ActiveCapturer);

public sealed record NetworkInterfaceInfo(
    string Id,
    string Name,
    string Description,
    string Ip,
    string Mac,
    string Type,
    long Speed,
    bool Up,
    bool Loopback);

public sealed record FlowSummary(
    string SrcIp, string DstIp, int SrcPort, int DstPort, string Protocol,
    int PacketCount, long TotalBytes, DateTime FirstSeen, DateTime LastSeen);

public sealed record ProtocolStat(
    string Protocol, int Count, long TotalBytes, double Percentage);

public sealed record TimeSeriesPoint(
    DateTime Timestamp, int PacketCount, long TotalBytes,
    IReadOnlyDictionary<string, int> Protocols);

public sealed record HostStat(
    string Ip, string? Name, int SentPackets, int ReceivedPackets,
    long SentBytes, long ReceivedBytes, int UniqueDestinations, int UniqueSources);

public sealed record AnomalyEvent(
    string Id, DateTime Timestamp, string Type, string Description,
    string? RelatedIp, int Severity);

public sealed record CommandPattern(
    string Signature, int Occurrences, double IntervalMs,
    string Description, IReadOnlyList<string> RelatedIps);

public sealed record PeriodComparison(
    int CurrentPackets, int PreviousPackets, double ChangePercent,
    int CurrentFlows, int PreviousFlows);

public sealed record TrafficReport(
    string ReportType,
    DateTime GeneratedAt,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalPackets,
    long TotalBytes,
    IReadOnlyList<ProtocolStat> Protocols,
    IReadOnlyList<FlowSummary> TopFlows,
    IReadOnlyList<HostStat> TopHosts,
    IReadOnlyList<TimeSeriesPoint> TimeSeries,
    IReadOnlyList<AnomalyEvent> Anomalies,
    IReadOnlyList<CommandPattern> CommandPatterns,
    PeriodComparison? Comparison,
    string? LlmSummary);
