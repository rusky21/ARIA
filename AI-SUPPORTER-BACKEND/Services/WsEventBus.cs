using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AI_Supporter;

public sealed class WsEventBus
{
    private readonly IBroadcastService _sse;
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WsEventBus(IBroadcastService sse)
    {
        _sse = sse;
    }

    private static byte[] Serialize(object value)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
            JsonSerializer.Serialize(writer, value, Json);
        return buffer.WrittenSpan.ToArray();
    }

    private void Publish(object payload) => _sse.SendJsonBytes(Serialize(payload));

    private void PublishToOwner(object payload) =>
        _sse.SendJsonBytes(Serialize(payload), RequestContext.Current.Value);

    public void SystemStats(double cpu, double ram, long ramTotal, long ramUsed, long ramFree,
        double gpu, long vramUsed, long vramLimit, string uptime, object processes,
        IReadOnlyList<TaskLoadInfo> taskLoads, double appRamPct, long appRamMb, double appGpu)
    {
        Publish(new
        {
            type = "system:stats",
            cpu,
            ram,
            ramTotal = $"{ramTotal} МБ",
            ramUsed = $"{ramUsed} МБ",
            ramFree = $"{ramFree} МБ",
            gpu,
            vramUsed = vramLimit > 0 ? FormatMb(vramUsed) : "",
            vramLimit = vramLimit > 0 ? FormatMb(vramLimit) : "",
            uptime,
            processes,
            tasks = taskLoads.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                icon = t.Icon,
                enabled = t.Enabled,
                continuous = t.Continuous,
                cpu = t.CpuPct,
                ram = t.Enabled ? appRamPct : (double?)null,
                ramMb = t.Enabled ? FormatMb(appRamMb) : null,
                gpu = t.Enabled ? appGpu : (double?)null,
                inFlight = t.InFlight,
                lastRun = t.LastRun?.ToString("HH:mm:ss"),
                nextRun = t.NextRun?.ToString("HH:mm:ss"),
                lastError = t.LastError
            })
        });
    }

    private static string FormatMb(long mb) =>
        mb >= 1024 ? $"{Math.Round(mb / 1024.0, 1)} ГБ" : $"{mb} МБ";

    private static object TrafficDto(TrafficRecord r) => new
    {
        time = r.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
        src = r.SrcIp,
        dst = r.DstIp,
        proto = r.Protocol,
        sport = r.SrcPort,
        dport = r.DstPort,
        len = r.Length,
        ttl = r.Ttl,
        raw = r.Raw is null ? null : Convert.ToHexString(r.Raw),
        msg = r.Summary,
        seq = r.Seq
    };

    public void TrafficLog(IReadOnlyList<TrafficRecord> records)
    {
        Publish(new
        {
            type = "traffic:log",
            replace = true,
            records = records.Select(TrafficDto),
            seq = records.Count > 0 ? records.Max(r => r.Seq) : 0
        });
    }

    public void TrafficDelta(IReadOnlyList<TrafficRecord> records, ulong seq)
    {
        if (records.Count == 0) return;
        Publish(new
        {
            type = "traffic:log",
            replace = false,
            records = records.Select(TrafficDto),
            seq
        });
    }

    public void TrafficCaptureState(bool running, ulong totalCaptured)
    {
        Publish(new { type = "traffic:capture", running, totalCaptured });
    }

    public void TrafficCleared()
    {
        Publish(new { type = "traffic:cleared" });
    }

    public void NetworkInterfaces(IReadOnlyList<NetworkInterfaceInfo> interfaces, string? currentIp)
    {
        Publish(new
        {
            type = "traffic:interfaces",
            interfaces = interfaces.Select(i => new
            {
                id = i.Id,
                name = i.Name,
                description = i.Description,
                ip = i.Ip,
                mac = i.Mac,
                type = i.Type,
                speed = i.Speed,
                up = i.Up,
                loopback = i.Loopback
            }),
            currentIp
        });
    }

    public void InterfaceSetResult(bool ok, string ip, string? error)
    {
        Publish(new { type = "traffic:interface:set", ok, ip, error });
    }

    public void IpRegistered(string ip, string name)
    {
        Publish(new { type = "traffic:ip_registered", ip, name });
    }

    public void TrafficQuery(IReadOnlyList<TrafficRecord> records, string query, object? args = null)
    {
        Publish(new
        {
            type = "traffic:query",
            records = records.Select(TrafficDto),
            query,
            args
        });
    }

    public void Patterns(IReadOnlyList<TrafficPattern> patterns)
    {
        Publish(new
        {
            type = "traffic:patterns",
            patterns = patterns.Select(p => new
            {
                id = p.Id,
                description = p.Description,
                related_ips = p.RelatedIps ?? [],
                suggested_action = p.SuggestedAction,
                detected_at = p.DetectedAt.ToString("O")
            })
        });
    }

    public void PatternsCleared()
    {
        Publish(new { type = "traffic:patterns_cleared" });
    }

    public void TrafficReport(TrafficReport report)
    {
        Publish(new
        {
            type = "traffic:report",
            report = new
            {
                reportType = report.ReportType,
                generatedAt = report.GeneratedAt.ToString("O"),
                periodStart = report.PeriodStart.ToString("O"),
                periodEnd = report.PeriodEnd.ToString("O"),
                totalPackets = report.TotalPackets,
                totalBytes = report.TotalBytes,
                protocols = report.Protocols.Select(p => new
                {
                    protocol = p.Protocol, count = p.Count,
                    totalBytes = p.TotalBytes, percentage = p.Percentage
                }),
                topFlows = report.TopFlows.Select(f => new
                {
                    srcIp = f.SrcIp, dstIp = f.DstIp,
                    srcPort = f.SrcPort, dstPort = f.DstPort, protocol = f.Protocol,
                    packetCount = f.PacketCount, totalBytes = f.TotalBytes,
                    firstSeen = f.FirstSeen.ToString("O"), lastSeen = f.LastSeen.ToString("O")
                }),
                topHosts = report.TopHosts.Select(h => new
                {
                    ip = h.Ip, name = h.Name,
                    sentPackets = h.SentPackets, receivedPackets = h.ReceivedPackets,
                    sentBytes = h.SentBytes, receivedBytes = h.ReceivedBytes,
                    uniqueDestinations = h.UniqueDestinations, uniqueSources = h.UniqueSources
                }),
                timeSeries = report.TimeSeries.Select(t => new
                {
                    timestamp = t.Timestamp.ToString("O"),
                    packetCount = t.PacketCount, totalBytes = t.TotalBytes,
                    protocols = t.Protocols
                }),
                anomalies = report.Anomalies.Select(a => new
                {
                    id = a.Id, timestamp = a.Timestamp.ToString("O"),
                    type = a.Type, description = a.Description,
                    relatedIp = a.RelatedIp, severity = a.Severity
                }),
                commandPatterns = report.CommandPatterns.Select(p => new
                {
                    signature = p.Signature, occurrences = p.Occurrences,
                    intervalMs = p.IntervalMs, description = p.Description,
                    relatedIps = p.RelatedIps
                }),
                comparison = report.Comparison,
                llmSummary = report.LlmSummary
            }
        });
    }

    public void TrafficExplanation(string targetId, string explanation)
    {
        Publish(new
        {
            type = "traffic:explanation",
            text = explanation
        });
    }

    public void Tasks(IReadOnlyList<TaskInfo> tasks)
    {
        Publish(new
        {
            type = "tasks:list",
            tasks = tasks.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                schedule = t.Schedule,
                desc = t.Description,
                icon = t.Icon,
                enabled = t.Enabled,
                lastRun = t.LastRun?.ToString("HH:mm:ss"),
                nextRun = t.NextRun?.ToString("HH:mm:ss"),
                lastError = t.LastError
            })
        });
    }

    public void LlmSettings(LlmSettings s)
    {
        Publish(new
        {
            type = "settings:llm",
            provider = s.Provider,
            local = new { url = s.Local.Url, model = s.Local.Model },
            api = new { baseUrl = s.Api.BaseUrl, model = s.Api.Model }
        });
    }

    public void LlmStatus(bool ok, long ms, string model)
    {
        Publish(new { type = "settings:llm:status", ok, ms, model });
    }

    public void LlmSettingsSet(bool ok, string? error)
    {
        Publish(new { type = "settings:llm:set", ok, error });
    }

    public void IpRegistry(IReadOnlyList<IpMapping> entries)
    {
        Publish(new
        {
            type = "ip-registry",
            entries = entries.Select(e => new
            {
                ip = e.Ip,
                name = e.Name,
                notes = e.Notes
            })
        });
    }

    public void ArtPollResult(IReadOnlyList<(string ip, string name)> devices)
    {
        Publish(new
        {
            type = "artnet:poll-result",
            devices = devices.Select(d => new { ip = d.ip, name = d.name })
        });
    }

    public void DeviceTestResult(string ip, IReadOnlyList<(string test, bool success, object details)> results)
    {
        Publish(new
        {
            type = "device:test-result",
            ip,
            results = results.Select(r => new { test = r.test, success = r.success, details = r.details })
        });
    }

    public void Devices(IReadOnlyList<DiscoveredDevice> devices)
    {
        Publish(new
        {
            type = "devices:list",
            devices = devices.Select(ProjectDevice)
        });
    }

    public void DeviceFound(DiscoveredDevice device)
    {
        Publish(new { type = "devices:found", device = ProjectDevice(device) });
    }

    private static object ProjectDevice(DiscoveredDevice d) => new
    {
        ip = d.Ip,
        name = d.Name,
        type = d.Type.ToString().ToLowerInvariant(),
        online = d.Online,
        services = d.Services,
        mjpegUrl = d.MjpegUrl,
        lastSeen = d.LastSeen.ToString("O"),
        mac = d.Mac,
        vendor = d.Vendor,
        responseMs = d.ResponseMs,
        notes = d.Notes
    };

    public void DevicesScanning(bool scanning)
    {
        Publish(new { type = "devices:scanning", scanning });
    }

    public void UIWindowOpen(string kind, string url, string title)
    {
        PublishToOwner(new { type = "ui:window:open", kind, url, title });
    }

    public void Chat(string text, bool done = false)
    {
        PublishToOwner(new { type = "chat", text, done });
    }

    public void ChatHistory(ChatMessage[] messages)
    {
        PublishToOwner(new { type = "chat:history", messages });
    }

    public void ChatHistoryCleared()
    {
        PublishToOwner(new { type = "chat:history:cleared" });
    }

    public void TerminalLine(string text, string? color = null)
    {
        PublishToOwner(new { type = "terminal", text, color = color ?? "#9fb0c4" });
    }

    public void VoiceTranscription(string text)
    {
        PublishToOwner(new { type = "voice:transcription", text });
    }

    public void VoiceResponse(string text)
    {
        PublishToOwner(new { type = "voice:response", text });
    }

    public void VoiceAudio(byte[] wavData)
    {
        PublishToOwner(new
        {
            type = "voice:audio",
            audio = Convert.ToBase64String(wavData)
        });
    }

    public void VoiceSpeakDone(bool ok, string? error = null)
    {
        PublishToOwner(new { type = "voice:speak:done", ok, error });
    }

    public void VoiceError(string message)
    {
        PublishToOwner(new { type = "voice:error", message });
    }

    public void TtsModels(string[] local)
    {
        PublishToOwner(new
        {
            type = "tts:models",
            local,
            api = TtsService.ApiVoices
        });
    }

    public void TtsConf(string engine, string voice, bool ok)
    {
        PublishToOwner(new { type = "tts:conf", engine, voice, ok });
    }

    public void TtsSetResult(bool ok, string? error)
    {
        PublishToOwner(new { type = "tts:set:result", ok, error });
    }

    public void TtsCheckResult(string engine, string voice, bool ok, long ms, string message)
    {
        PublishToOwner(new { type = "tts:check:result", engine, voice, ok, ms, message });
    }

    public void WakeConfig(bool enabled, float threshold, IReadOnlyList<List<List<float>>>? templates)
    {
        var list = templates ?? Array.Empty<List<List<float>>>();
        Publish(new
        {
            type = "wake:config",
            enabled,
            threshold = Math.Round(threshold, 3),
            trained = list.Count > 0,
            templates = list.Select(t => new
            {
                frames = t.Count,
                durationSec = Math.Round(t.Count * WakeDsp.FrameHop / (double)WakeDsp.SampleRate, 2)
            })
        });
    }

    public void Navigate(string screen)
    {
        PublishToOwner(new { type = "nav", screen });
    }

    public void Popup(string title, string body, string? meta = null, string accent = "#ff6b5b", bool hasMeta = false)
    {
        PublishToOwner(new
        {
            type = "popup",
            title,
            body,
            meta,
            accent,
            hasMeta
        });
    }

    public void DevAuthResult(bool success, bool canChange)
    {
        PublishToOwner(new { type = "dev:auth:result", success, canChange });
    }

    public void DevResponse(string text)
    {
        PublishToOwner(new { type = "dev:response", text });
    }

    public void DevThinking(string? text = null)
    {
        PublishToOwner(new { type = "dev:thinking", text });
    }

    public void DevReasoning(string text)
    {
        PublishToOwner(new { type = "dev:reasoning", text });
    }

    public void DevOptions(DevAgentInfo[] agents, string[] models)
    {
        PublishToOwner(new { type = "dev:options:result", agents, models });
    }

    public void DevDone()
    {
        PublishToOwner(new { type = "dev:done" });
    }

    public void DevChanges(DevFileChange[] files)
    {
        PublishToOwner(new { type = "dev:changes", files });
    }

    public void DevApplied()
    {
        PublishToOwner(new { type = "dev:applied" });
    }

    public void DevError(string message)
    {
        PublishToOwner(new { type = "dev:error", message });
    }

    public void DevHistory(DevChatMessage[] messages)
    {
        PublishToOwner(new { type = "dev:history", messages });
    }

    public void ChatReasoning(string text) => PublishToOwner(new { type = "chat:reasoning", text });
    public void ChatThinking(string? text = null) => PublishToOwner(new { type = "chat:thinking", text });
    public void ChatList(IReadOnlyList<ConversationMeta> conversations) => PublishToOwner(new { type = "chat:list", conversations });
    public void ChatCreated(ConversationMeta meta) => PublishToOwner(new { type = "chat:created", id = meta.Id, title = meta.Title, createdAt = meta.CreatedAt, lastMessageAt = meta.LastMessageAt });
    public void ChatSwitched(string conversationId, ChatMessage[] messages) => PublishToOwner(new { type = "chat:switched", conversationId, messages });
    public void ChatDeleted(string conversationId) => PublishToOwner(new { type = "chat:deleted", conversationId });
    public void ChatRenamed(string conversationId, string title) => PublishToOwner(new { type = "chat:renamed", conversationId, title });

    // ── File system ──

    public void FsListResult(string path, string? parent, object entries)
    {
        PublishToOwner(new { type = "fs:list:result", path, parent, entries });
    }

    public void FsReadResult(string? path, string? content, bool truncated, long size, bool binary)
    {
        PublishToOwner(new { type = "fs:read:result", path, content, truncated, size, binary });
    }

    // ── User management ──

    public void UserListResult(object users)
    {
        PublishToOwner(new { type = "user:list:result", users });
    }

    public void UserResult(bool ok, string? error)
    {
        PublishToOwner(new { type = "user:result", ok, error });
    }

    // ── Traffic export ──

    public void TrafficExportResult(bool ok, string data, string filename, long size)
    {
        PublishToOwner(new { type = "traffic:export:result", ok, data, filename, size });
    }

    // ── Traffic filter ──

    public void TrafficFilterResult(object records)
    {
        PublishToOwner(new { type = "traffic:filter:result", records });
    }

    // ── Wake word train/check/reset ──

    public void WakeTrainResult(bool ok, int frames, float durationSec, int templatesCount, string? error)
    {
        PublishToOwner(new { type = "wake:train:result", ok, frames, durationSec, templatesCount, error });
    }

    public void WakeResetResult(bool ok)
    {
        PublishToOwner(new { type = "wake:reset:result", ok });
    }

    public void WakeCheckResult(bool isHotword, float score, float threshold)
    {
        PublishToOwner(new { type = "wake:check:result", isHotword, score, threshold });
    }

    // ── LLM Profile & Capabilities ──

    public void LlmProfile(UserLlmProfile profile)
    {
        PublishToOwner(new { type = "llm:profile", profile });
    }

    public void LlmCapabilities(CapabilitiesDto caps)
    {
        PublishToOwner(new { type = "llm:capabilities", caps });
    }

    public void LlmProfileSaved(bool ok)
    {
        PublishToOwner(new { type = "llm:profile:saved", ok });
    }

    public void LlmPingResult(string provider, string requestedModel, bool ok, long ms, string resolvedModel)
    {
        PublishToOwner(new { type = "llm:ping:result", provider, model = resolvedModel ?? requestedModel, ok, ms });
    }

    public void LlmResolveResult(string? task, string provider, string model, bool ok)
    {
        PublishToOwner(new { type = "llm:resolve:result", task, provider, model, ok });
    }

    public void AsrModels()
    {
        PublishToOwner(new
        {
            type = "asr:models",
            local = new[] { "tiny", "base", "small", "medium", "large-v3" },
            api = new[] { "openai/gpt-4o-mini-transcribe", "openai/gpt-4o-transcribe", "openai/gpt-4o-transcribe-diarize" }
        });
    }

    public void AsrCheckResult(string provider, string? model, bool ok, long ms, string engine, string message)
    {
        PublishToOwner(new { type = "asr:check:result", provider, model, ok, ms, engine, message });
    }

    public void CameraSnapshot(string ip, bool ok, string data, string message = "")
    {
        PublishToOwner(new { type = "camera:snapshot:result", ip, ok, data, message });
    }

    public void CameraAnalyze(string ip, bool ok, string result)
    {
        PublishToOwner(new { type = "camera:analyze:result", ip, ok, result });
    }
}
