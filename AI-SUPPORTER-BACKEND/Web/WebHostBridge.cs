using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI_Supporter;

public sealed class WebHostBridge
{
    private readonly HttpListener _listener = new();
    private readonly Func<string, Task> _onMessage;
    private readonly Func<string, Task<string?>>? _onVoiceProcess;
    public event Action<string>? SystemMessage;
    private static readonly JsonSerializerOptions _jsonUnsafe = AppJson.Api;

    private readonly object _sseLock = new();
    private readonly List<HttpListenerResponse> _sseClients = new();
    private readonly object _wsLock = new();
    private readonly List<WebSocket> _wsClients = new();
    private WhisperService? _whisperService;
    private CancellationTokenSource? _cts;

    public WebHostBridge(Func<string, Task> onMessage, Func<string, Task<string?>>? onVoiceProcess = null, string prefix = "http://+:5000/")
    {
        _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
        _onVoiceProcess = onVoiceProcess;
        Console.WriteLine($"[WEB] Binding to {prefix}");
        _listener.Prefixes.Add(prefix);
    }

    public bool WebServerStarted { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            Console.WriteLine("[WEB] Starting listener...");
            _listener.Start();
            WebServerStarted = true;
            Console.WriteLine("[WEB] STARTED OK");
            Console.WriteLine(" - http://localhost:5000");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[WEB] START FAILED:");
            Console.WriteLine(ex.ToString());
            return;
        }

        while (!_cts.Token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine("[WEB] Listener stopped: " + ex.Message);
                break;
            }
            _ = HandleAsync(ctx);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            
            if (path.Equals("/ws", StringComparison.OrdinalIgnoreCase) && ctx.Request.IsWebSocketRequest)
            {
                await HandleWebSocketAsync(ctx);
                return;
            }

            if (path.Equals("/send", StringComparison.OrdinalIgnoreCase) && 
                string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var message = ExtractMessage(body);
                Console.WriteLine($"[FROM WEB]: {message}");
                _ = _onMessage(message);
                SystemMessage?.Invoke($"[FROM WEB]: {message}");
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("OK"));
                ctx.Response.Close();
                return;
            }

            if (path.Equals("/events", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.Headers.Add("Connection", "keep-alive");
                lock (_sseLock)
                {
                    _sseClients.Add(ctx.Response);
                }
                try
                {
                    await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: connected\n\n"));
                    await ctx.Response.OutputStream.FlushAsync();
                    await Task.Delay(Timeout.InfiniteTimeSpan, _cts?.Token ?? CancellationToken.None);
                }
                catch (OperationCanceledException) { }
                catch
                {
                    lock (_sseLock)
                    {
                        _sseClients.Remove(ctx.Response);
                    }
                }
                finally
                {
                    lock (_sseLock)
                    {
                        _sseClients.Remove(ctx.Response);
                    }
                }
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApiAsync(ctx, path);
                return;
            }

            await ServeStaticFileAsync(ctx, path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WebHost error: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private async Task HandleApiAsync(HttpListenerContext ctx, string path)
    {
        var method = ctx.Request.HttpMethod.ToUpperInvariant();

        try
        {
            if (path.Equals("/api/traffic/log", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                if (_sniffer is null) { WriteJson(ctx, new { error = "not ready" }); return; }
                var records = _sniffer.GetRecent(500);
                WriteJson(ctx, records.Select(r => new
                {
                    time = r.Timestamp.ToString("HH:mm:ss"),
                    src = r.SrcIp, dst = r.DstIp,
                    proto = r.Protocol, sport = r.SrcPort, dport = r.DstPort,
                    len = r.Length, msg = r.Summary
                }));
                return;
            }

            if (path.Equals("/api/traffic/stats", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                if (_sniffer is null) { WriteJson(ctx, new { error = "not ready" }); return; }
                WriteJson(ctx, _sniffer.GetStats());
                return;
            }

            if (path.Equals("/api/traffic/patterns", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                if (_analyzer is null) { WriteJson(ctx, new { error = "not ready" }); return; }
                var patterns = _analyzer.GetPatterns();
                WriteJson(ctx, patterns.Select(p => new
                {
                    id = p.Id, description = p.Description,
                    related_ips = p.RelatedIps ?? [], suggested_action = p.SuggestedAction,
                    detected_at = p.DetectedAt.ToString("O")
                }));
                return;
            }

            if (path.Equals("/api/traffic/clear", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                if (_sniffer is null) { WriteJson(ctx, new { error = "not ready" }); return; }
                _sniffer.Clear();
                _analyzer?.ClearPatterns();
                WriteJson(ctx, new { ok = true });
                return;
            }

            if (path.Equals("/api/traffic/patterns/clear", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                if (_analyzer is null) { WriteJson(ctx, new { error = "not ready" }); return; }
                _analyzer.ClearPatterns();
                WriteJson(ctx, new { ok = true });
                return;
            }

            if (path.Equals("/api/traffic/test-data", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                if (_sniffer is null) { WriteJson(ctx, new { error = "not ready" }); return; }
                GenerateTestData();
                WriteJson(ctx, new { ok = true, count = 20 });
                return;
            }

            if (path.Equals("/api/vulnerabilities", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                if (VulnerabilityCache.LastCheck is null)
                {
                    WriteJson(ctx, new { error = "Проверка ещё не выполнялась" });
                    return;
                }
                WriteJson(ctx, new
                {
                    hasVulnerabilities = VulnerabilityCache.HasVulnerabilities,
                    details = VulnerabilityCache.Details,
                    lastCheck = VulnerabilityCache.LastCheck.Value.ToString("yyyy-MM-dd HH:mm:ss")
                });
                return;
            }

            if (path.Equals("/api/osc/status", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                if (_oscServer is null) { WriteJson(ctx, new { error = "osc server not ready" }); return; }
                var last = _oscServer.LastReceived;
                WriteJson(ctx, new { lastReceived = last is null ? null : new { path = last.Path, value = last.Value, from = last.SenderIp, time = last.Timestamp.ToString("HH:mm:ss") } });
                return;
            }

            if (path.Equals("/api/artnet/poll", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                if (_artnet is null) { WriteJson(ctx, new { error = "artnet not ready" }); return; }
                if (_sniffer is null) { WriteJson(ctx, new { error = "sniffer not ready" }); return; }
                var ips = await _artnet.SendPollAsync(_sniffer);
                if (ips.Count == 0)
                {
                    var fakeDevices = SimulatedIps.Select(ip => new
                    {
                        ip,
                        name = _ipRegistry?.Lookup(ip) ?? "Unknown Device"
                    }).ToArray();
                    WriteJson(ctx, new { devices = fakeDevices });
                }
                else
                {
                    var devices = ips.Select(ip => new
                    {
                        ip,
                        name = _ipRegistry?.Lookup(ip) ?? "Unknown Device"
                    });
                    WriteJson(ctx, new { devices });
                }
                return;
            }

            if (path.Equals("/api/test/device", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                var reqBody = await reader.ReadToEndAsync();
                var reqDoc = JsonDocument.Parse(reqBody);
                var testIp = reqDoc.RootElement.GetProperty("ip").GetString();
                var testList = reqDoc.RootElement.GetProperty("tests").EnumerateArray().Select(t => t.GetString()).Where(t => t != null).Cast<string>().ToList();

                if (string.IsNullOrWhiteSpace(testIp))
                {
                    WriteJson(ctx, new { error = "ip required" }, 400);
                    return;
                }

                var results = new List<object>();

                if (testList.Contains("ping"))
                {
                    try
                    {
                        if (SimulatedIps.Contains(testIp))
                        {
                            results.Add(new { test = "ping", success = true, details = new { ms = 0, error = (string?)null } });
                        }
                        else
                        {
                            var ping = new System.Net.NetworkInformation.Ping();
                            var reply = await ping.SendPingAsync(testIp, 3000);
                            var ok = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                            results.Add(new { test = "ping", success = ok, details = new { ms = (int)reply.RoundtripTime, error = ok ? null : reply.Status.ToString() } });
                        }
                    }
                    catch (Exception ex) { results.Add(new { test = "ping", success = false, details = new { ms = 0, error = ex.Message } }); }
                }

                if (testList.Contains("http"))
                {
                    try
                    {
                        if (SimulatedIps.Contains(testIp))
                        {
                            results.Add(new { test = "http", success = true, details = new { statusCode = 200, error = (string?)null } });
                        }
                        else
                        {
                            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                            var resp = await http.GetAsync($"http://{testIp}");
                            results.Add(new { test = "http", success = true, details = new { statusCode = (int)resp.StatusCode, error = (string?)null } });
                        }
                    }
                    catch (Exception ex) { results.Add(new { test = "http", success = false, details = new { statusCode = 0, error = ex.Message } }); }
                }

                if (testList.Contains("osc"))
                {
                    try
                    {
                        if (_osc is null) { results.Add(new { test = "osc", success = false, details = new { command = "/ping", error = "osc not ready" } }); }
                        else if (_oscServer is null) { results.Add(new { test = "osc", success = false, details = new { command = "/ping", error = "osc server not ready" } }); }
                        else
                        {
                            var (ok, detail) = await _osc.SendAndVerifyAsync("/ping", testIp, _oscServer);
                            results.Add(new { test = "osc", success = ok, details = new { command = "/ping", error = ok ? (string?)null : detail, detail = ok ? detail : (string?)null } });
                        }
                    }
                    catch (Exception ex) { results.Add(new { test = "osc", success = false, details = new { command = "/ping", error = ex.Message } }); }
                }

                if (testList.Contains("artnet"))
                {
                    try
                    {
                        if (_artnet is null) { results.Add(new { test = "artnet", success = false, details = new { universe = 0, channel = 1, value = 255, error = "artnet not ready" } }); }
                        else if (SimulatedIps.Contains(testIp))
                        {
                            results.Add(new { test = "artnet", success = true, details = new { universe = 0, channel = 1, value = 255, error = (string?)null } });
                        }
                        else
                        {
                            await _artnet.SendBroadcastAsync(0, 1, 255);
                            _ = Task.Run(async () => { await Task.Delay(1000); await _artnet.SendBroadcastAsync(0, 1, 0); });
                            results.Add(new { test = "artnet", success = true, details = new { universe = 0, channel = 1, value = 255, error = (string?)null } });
                        }
                    }
                    catch (Exception ex) { results.Add(new { test = "artnet", success = false, details = new { universe = 0, channel = 1, value = 255, error = ex.Message } }); }
                }

                WriteJson(ctx, new { ip = testIp, results });
                return;
            }

            if (path.Equals("/api/voice/transcribe", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                if (_whisperService is null)
                {
                    WriteJson(ctx, new { error = "whisper not ready" }, 500);
                    return;
                }

                try
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.InputStream.CopyToAsync(ms);
                    var audioData = ms.ToArray();

                    if (audioData.Length == 0)
                    {
                        WriteJson(ctx, new { error = "empty audio" }, 400);
                        return;
                    }

                    await _whisperService.EnsureModelAsync();

                    var user = RequestContext.Current.Value ?? "";
                    var speech = _userStore?.GetLlmProfile(user).Speech ?? new SpeechSelection();
                    var text = await _whisperService.TranscribeAsync(audioData, speech.Provider, speech.Model, speech.Lang);
                    WriteJson(ctx, new { text = text ?? "", ok = text != null });
                }
                catch (Exception ex)
                {
                    WriteJson(ctx, new { error = ex.Message, ok = false }, 500);
                }
                return;
            }

            if (path.Equals("/api/voice/wake", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                try
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.InputStream.CopyToAsync(ms);
                    var audioData = ms.ToArray();

                    if (audioData.Length == 0)
                    {
                        WriteJson(ctx, new { isHotword = false }, 200);
                        return;
                    }

                    if (_wakeWord is null || _whisperService is null)
                    {
                        WriteJson(ctx, new { isHotword = false }, 200);
                        return;
                    }

                    await _whisperService.EnsureModelAsync();

                    var isHotword = await _wakeWord.CheckAsync(audioData);
                    WriteJson(ctx, new { isHotword });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WAKE] Error: {ex.Message}");
                    WriteJson(ctx, new { isHotword = false }, 200);
                }
                return;
            }

            if (path.Equals("/api/voice/process", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                try
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    JsonDocument reqDoc;
                    try
                    {
                        reqDoc = JsonDocument.Parse(body);
                    }
                    catch (JsonException)
                    {
                        WriteJson(ctx, new { ok = false, response = "invalid json" }, 400);
                        return;
                    }
                    using (reqDoc)
                    {
                        var text = reqDoc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            WriteJson(ctx, new { ok = false, response = "text is required" }, 400);
                            return;
                        }

                        // Fast path: check navigation keywords before calling LLM
                        var navResult = DetectNav(text);
                        if (navResult is not null)
                        {
                            WriteJson(ctx, new { ok = true, response = navResult.Value.response, nav = navResult.Value.nav });
                            return;
                        }

                        if (_onVoiceProcess is null)
                        {
                            WriteJson(ctx, new { ok = false, response = "voice processing not available" }, 503);
                            return;
                        }

                        var llmResponse = await _onVoiceProcess(text);
                        WriteJson(ctx, new { ok = true, response = llmResponse ?? "Готово." });
                    }
                }
                catch (Exception ex)
                {
                    WriteJson(ctx, new { ok = false, response = $"Ошибка: {ex.Message}" }, 500);
                }
                return;
            }

            if (path.Equals("/api/voice/speak", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                try
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    JsonDocument reqDoc;
                    try
                    {
                        reqDoc = JsonDocument.Parse(body);
                    }
                    catch (JsonException)
                    {
                        WriteJson(ctx, new { ok = false, error = "invalid json" }, 400);
                        return;
                    }
                    using (reqDoc)
                    {
                        var text = reqDoc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            WriteJson(ctx, new { ok = false, error = "text is required" }, 400);
                            return;
                        }

                        if (_tts is null)
                        {
                            WriteJson(ctx, new { ok = false, error = "TTS not available" }, 503);
                            return;
                        }

                        var audio = await _tts.SynthesizeAsync(text);
                        if (audio is null || audio.Length == 0)
                        {
                            WriteJson(ctx, new { ok = false, error = "synthesis failed" }, 500);
                            return;
                        }

                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "audio/wav";
                        ctx.Response.ContentLength64 = audio.Length;
                        await ctx.Response.OutputStream.WriteAsync(audio);
                        ctx.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    WriteJson(ctx, new { ok = false, error = ex.Message }, 500);
                }
                return;
            }

            if (path.Equals("/api/system/stats", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                try
                {
                    var proc = System.Diagnostics.Process.GetCurrentProcess();
                    var totalMem = GC.GetTotalMemory(false);
                    var allProcs = System.Diagnostics.Process.GetProcesses()
                        .OrderByDescending(p => p.TotalProcessorTime.TotalMilliseconds)
                        .Take(15)
                        .Select(p =>
                        {
                            try { return new { pid = p.Id, name = p.ProcessName, cpu = 0, mem = $"{p.WorkingSet64 / 1024 / 1024} МБ" }; }
                            catch { return new { pid = 0, name = "?", cpu = 0, mem = "?" }; }
                        })
                        .ToArray();

                    var uptimeSpan = DateTime.UtcNow - proc.StartTime.ToUniversalTime();
                    var totalCpuMs = proc.TotalProcessorTime.TotalMilliseconds;
                    var cpuPercent = uptimeSpan.TotalMilliseconds > 0
                        ? Math.Round(totalCpuMs / uptimeSpan.TotalMilliseconds / Environment.ProcessorCount * 100, 1)
                        : 0.0;

                    var workingSet = proc.WorkingSet64;
                    var totalAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    var ramPercent = totalAvailable > 0
                        ? Math.Round((double)workingSet / totalAvailable * 100, 1)
                        : Math.Round(workingSet / (double)(2L * 1024 * 1024 * 1024) * 100, 1);

                    WriteJson(ctx, new
                    {
                        cpu = cpuPercent,
                        ram = ramPercent,
                        uptime = uptimeSpan.TotalHours > 1
                            ? $"{(int)uptimeSpan.TotalHours}ч {uptimeSpan.Minutes}м"
                            : $"{uptimeSpan.Minutes}м {uptimeSpan.Seconds}с",
                        processes = allProcs,
                    });
                }
                catch (Exception ex)
                {
                    WriteJson(ctx, new { error = ex.Message }, 500);
                }
                return;
            }

            if (path.Equals("/api/ip-registry", StringComparison.OrdinalIgnoreCase))
            {
                if (_ipRegistry is null) { WriteJson(ctx, new { error = "not ready" }); return; }

                if (method == "GET")
                {
                    WriteJson(ctx, _ipRegistry.GetAll().Select(m => new { ip = m.Ip, name = m.Name, notes = m.Notes }));
                    return;
                }

                if (method == "POST")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(body);
                    }
                    catch (JsonException)
                    {
                        WriteJson(ctx, new { error = "invalid json" }, 400);
                        return;
                    }
                    using (doc)
                    {
                        if (!doc.RootElement.TryGetProperty("ip", out var ipEl) || !doc.RootElement.TryGetProperty("name", out var nameEl))
                        {
                            WriteJson(ctx, new { error = "ip and name required" }, 400);
                            return;
                        }
                        var ip = ipEl.GetString();
                        var name = nameEl.GetString();
                        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(name))
                        {
                            WriteJson(ctx, new { error = "ip and name must be non-empty" }, 400);
                            return;
                        }
                        _ipRegistry.Register(ip, name);
                        WriteJson(ctx, new { ok = true, ip, name });
                    }
                    return;
                }
            }

            WriteJson(ctx, new { error = "not found" }, 404);
        }
        catch (Exception ex)
        {
            WriteJson(ctx, new { error = ex.Message }, 500);
        }
    }

    private static void WriteJson(HttpListenerContext ctx, object data, int statusCode = 200)
    {
        var json = JsonSerializer.Serialize(data, _jsonUnsafe);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    private async Task HandleWebSocketAsync(HttpListenerContext ctx)
    {
        WebSocket? ws = null;
        try
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            ws = wsCtx.WebSocket;

            lock (_wsLock)
            {
                _wsClients.Add(ws);
            }
            Console.WriteLine("[WS] Client connected");

            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"[WS] {text}");
                    
                    var reqId = Guid.NewGuid().ToString("N")[..8];
                    RequestContext.Current.Value = reqId;
                    
                    try { await _onMessage(text); }
                    finally { RequestContext.Current.Value = null; }
                    
                    SystemMessage?.Invoke($"[FROM WS]: {text}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS] Error: {ex.Message}");
        }
        finally
        {
            if (ws != null)
            {
                lock (_wsLock)
                {
                    _wsClients.Remove(ws);
                }
            }
        }
    }

    public void Broadcast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        
        var payload = message.Replace("\r", " ").Replace("\n", " ");
        
        var ssePayload = $"data: {payload}\n\n";
        var sseBytes = Encoding.UTF8.GetBytes(ssePayload);
        List<HttpListenerResponse> sseClients;
        lock (_sseLock)
        {
            sseClients = _sseClients.ToList();
        }
        foreach (var client in sseClients)
        {
            try
            {
                client.OutputStream.Write(sseBytes, 0, sseBytes.Length);
                client.OutputStream.Flush();
            }
            catch
            {
                lock (_sseLock) { _sseClients.Remove(client); }
            }
        }

        var wsBytes = Encoding.UTF8.GetBytes(payload);
        List<WebSocket> wsList;
        lock (_wsLock)
        {
            wsList = _wsClients.ToList();
        }
        foreach (var ws in wsList)
        {
            if (ws.State == WebSocketState.Open)
            {
                _ = SendWsMessageAsync(ws, wsBytes);
            }
        }
    }

    private static async Task SendWsMessageAsync(WebSocket ws, byte[] bytes)
    {
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // client will be cleaned up on next failed receive or close frame
        }
    }

    public void BroadcastChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var json = JsonSerializer.Serialize(new { type = "chat", text = message }, _jsonUnsafe);
        SendToAll(json);
    }

    public void BroadcastLog(string message, string? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var json = JsonSerializer.Serialize(new { type = "log", text = message, requestId }, _jsonUnsafe);
        SendToAll(json);
    }

    public void BroadcastJson(object data, string? requestId = null)
    {
        var wrapper = new { requestId, payload = data };
        var json = JsonSerializer.Serialize(new { type = "json", requestId, payload = data }, _jsonUnsafe);
        SendToAll(json);
    }

    private void SendToAll(string json)
    {
        var ssePayload = $"data: {json}\n\n";
        var sseBytes = Encoding.UTF8.GetBytes(ssePayload);
        List<HttpListenerResponse> sseClients;
        lock (_sseLock)
        {
            sseClients = _sseClients.ToList();
        }
        foreach (var client in sseClients)
        {
            try
            {
                client.OutputStream.Write(sseBytes, 0, sseBytes.Length);
                client.OutputStream.Flush();
            }
            catch
            {
                lock (_sseLock) { _sseClients.Remove(client); }
            }
        }

        var wsBytes = Encoding.UTF8.GetBytes(json);
        List<WebSocket> wsList;
        lock (_wsLock)
        {
            wsList = _wsClients.ToList();
        }
        foreach (var ws in wsList)
        {
            if (ws.State == WebSocketState.Open)
            {
                _ = SendWsMessageAsync(ws, wsBytes);
            }
        }
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var parts = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("msg=", StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.UrlDecode(part[4..]);
            }
        }
        return WebUtility.UrlDecode(body);
    }

    private static (string nav, string response)? DetectNav(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lower = text.ToLowerInvariant();

        if (Regex.IsMatch(lower, @"сет|network|скан|устройств|wireshark|трафик"))
            return ("network", "Открываю сетевой анализ.");

        if (Regex.IsMatch(lower, @"систем|cpu|памят|процесс|загруз"))
            return ("system", "Открываю монитор системы.");

        if (Regex.IsMatch(lower, @"термин|команд|консол|shell"))
            return ("terminal", "Открываю терминал.");

        if (Regex.IsMatch(lower, @"файл|лог|log"))
            return ("files", "Открываю файлы и логи.");

        if (Regex.IsMatch(lower, @"задач|автомат|расписан"))
            return ("tasks", "Открываю задачи.");

        if (Regex.IsMatch(lower, @"настрой|голос|setting"))
            return ("settings", "Открываю настройки.");

        if (Regex.IsMatch(lower, @"чат|chat"))
            return ("chat", "Открываю чат.");

        return null;
    }

    private static readonly HashSet<string> SimulatedIps = new(StringComparer.OrdinalIgnoreCase)
    {
        "192.168.1.100", "192.168.1.50", "10.0.0.1", "10.0.0.2", "172.16.0.10"
    };

    private static readonly string? _wwwroot = ResolveWwwRoot();
    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".json"] = "application/json; charset=utf-8",
    };

    private static string? ResolveWwwRoot()
    {
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var candidates = new List<string>
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "dist"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "dist"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "dist"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "frontend", "dist"),
            Path.Combine(projectDir, "..", "AI-SUPPORTER-FRONTEND", "dist"),
        };
        candidates.AddRange(FindSiblingDists(projectDir));
        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    private static IEnumerable<string> FindSiblingDists(string projectDir)
    {
        var found = new List<string>();
        try
        {
            var parent = Directory.GetParent(projectDir);
            if (parent is null)
                return found;

            foreach (var dir in Directory.EnumerateDirectories(parent.FullName))
            {
                if (dir.Equals(projectDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = Path.Combine(dir, "dist");
                if (File.Exists(Path.Combine(candidate, "index.html")))
                    found.Add(candidate);
            }
        }
        catch
        {
        }

        return found;
    }

    private static async Task ServeStaticFileAsync(HttpListenerContext ctx, string path)
    {
        var wwwRoot = Path.GetFullPath(_wwwroot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"));
        var relativePath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(wwwRoot, relativePath));

        if (!fullPath.StartsWith(wwwRoot, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }

        var filePath = fullPath;

        if (string.IsNullOrEmpty(Path.GetExtension(path)) || path.Equals("/", StringComparison.Ordinal))
        {
            filePath = Path.Combine(wwwRoot, "index.html");
        }

        if (!File.Exists(filePath))
        {
            var indexHtml = Path.Combine(_wwwroot ?? "wwwroot", "index.html");
            if (File.Exists(indexHtml))
            {
                filePath = indexHtml;
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                var msg = Encoding.UTF8.GetBytes("Not found");
                ctx.Response.ContentLength64 = msg.Length;
                await ctx.Response.OutputStream.WriteAsync(msg);
                ctx.Response.Close();
                Console.WriteLine($"[WEB] 404: {path}");
                return;
            }
        }

        try
        {
            var ext = Path.GetExtension(filePath);
            ctx.Response.ContentType = _mimeTypes.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
            var bytes = await File.ReadAllBytesAsync(filePath);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WEB] Error serving {filePath}: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private IpRegistryService? _ipRegistry;
    private TrafficSnifferService? _sniffer;
    private TrafficAnalyzerService? _analyzer;
    private ArtnetService? _artnet;
    private OscService? _osc;
    private OscServerService? _oscServer;
    private TtsService? _tts;
    private WakeWordService? _wakeWord;
    private UserStore? _userStore;

    public void SetUserStore(UserStore userStore)
    {
        _userStore = userStore;
    }

    public void SetWhisperService(WhisperService service)
    {
        _whisperService = service;
    }

    public void SetArtnetService(ArtnetService artnet)
    {
        _artnet = artnet;
    }

    public void SetOscService(OscService osc)
    {
        _osc = osc;
    }

    public void SetOscServerService(OscServerService oscServer)
    {
        _oscServer = oscServer;
    }

    public void SetTtsService(TtsService tts)
    {
        _tts = tts;
    }

    public void SetWakeWordService(WakeWordService wakeWord)
    {
        _wakeWord = wakeWord;
    }

    public void SetApiServices(IpRegistryService ipRegistry, TrafficSnifferService sniffer, TrafficAnalyzerService analyzer)
    {
        _ipRegistry = ipRegistry;
        _sniffer = sniffer;
        _analyzer = analyzer;
    }

    public void OnSystemMessage(string message)
    {
        Broadcast(message);
    }

    public void BroadcastJson(object data)
    {
        var json = JsonSerializer.Serialize(data, _jsonUnsafe);
        Broadcast(json);
    }

    private void GenerateTestData()
    {
        if (_sniffer is null) return;
        var now = DateTime.UtcNow;
        var testRecords = new[]
        {
            (ip: "192.168.1.100", name: "ArtNet Controller"),
            (ip: "192.168.1.50",  name: "OSC Mixer"),
            (ip: "10.0.0.1",      name: "Lighting Console"),
            (ip: "10.0.0.2",      name: "LED Panels"),
            (ip: "172.16.0.10",   name: "Media Server"),
            (ip: "10.10.10.10",   name: "Unknown Device"),
        };

        foreach (var (ip, name) in testRecords)
            _ipRegistry?.Register(ip, name);

        // Pattern 1: ArtNet DMX на соседние каналы 1-5 за 0.5с → group
        var artnetGroup = new[]
        {
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=1 val=255 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=2 val=128 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=3 val=200 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=4 val=50 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=5 val=180 (группировка: соседние каналы 1-5)"),
        };

        // Pattern 2: OSC FloatSlider циклически 0→0.25→0.5→0.75→1.0→0→... → macro
        var oscMacro = new[]
        {
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0 (цикл: нарастание 0→1)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0.25 (цикл: нарастание 0→1)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0.5 (цикл: нарастание 0→1)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0.75 (цикл: нарастание 0→1)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=1.0 (цикл: пик)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0 (цикл: сброс к 0)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0.25 (цикл: повтор)"),
            (s: "192.168.1.50", d: "10.0.0.1", p: "OSC", sp: 8000, dp: 8000, len: 64, msg: "/avatar/parameters/FloatSlider=0.5 (цикл: повтор)"),
        };

        // Pattern 3: ArtNet Poll периодически каждые ~5с → macro
        var artnetPoll = new[]
        {
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
        };

        // Pattern 4: Неизвестный IP 10.10.10.10 шлёт много ArtNet → alert
        var alertFlood = new[]
        {
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
        };

        // Normal traffic
        var normal = new[]
        {
            (s: "172.16.0.10", d: "192.168.1.100", p: "TCP", sp: 443, dp: 34512, len: 1200, msg: "TCP port 443->34512"),
            (s: "192.168.1.100", d: "8.8.8.8", p: "UDP", sp: 56789, dp: 53, len: 80, msg: null),
            (s: "10.0.0.1", d: "8.8.8.8", p: "ICMP", sp: 0, dp: 0, len: 84, msg: null),
        };

        var offset = 0;
        foreach (var s in artnetGroup)
        {
            var ts = now.AddMilliseconds(-(22000 - offset * 100));
            _sniffer.AddRecord(new TrafficRecord(ts, s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }

        offset += 5;
        foreach (var s in oscMacro)
        {
            var ts = now.AddMilliseconds(-(22000 - offset * 100));
            _sniffer.AddRecord(new TrafficRecord(ts, s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }

        offset += 2;
        foreach (var s in artnetPoll)
        {
            var ts = now.AddMilliseconds(-(22000 - offset * 100));
            _sniffer.AddRecord(new TrafficRecord(ts, s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }

        offset += 2;
        foreach (var s in alertFlood)
        {
            var ts = now.AddMilliseconds(-(22000 - offset * 100));
            _sniffer.AddRecord(new TrafficRecord(ts, s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }

        offset += 1;
        foreach (var s in normal)
        {
            var ts = now.AddMilliseconds(-(22000 - offset * 100));
            _sniffer.AddRecord(new TrafficRecord(ts, s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }

        Console.WriteLine($"[WEB] Injected {offset} test traffic records with patterns");
    }
}
