using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;

namespace AI_Supporter;

public static class ApiEndpoints
{
    private static readonly JsonSerializerOptions _json = AppJson.Api;

    private static readonly HashSet<string> SimulatedIps = new(StringComparer.OrdinalIgnoreCase)
    {
        "192.168.1.100", "192.168.1.50", "10.0.0.1", "10.0.0.2", "172.16.0.10"
    };

    public static void MapApiEndpoints(this WebApplication app, WebHostServices services)
    {
        var api = app.MapGroup("/api");
        api.RequireAuthorization();

        api.MapPost("/auth/login", [AllowAnonymous] async (HttpRequest request) =>
        {
            var clientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!RateLimiter.Allow($"login:{clientIp}", 10, TimeSpan.FromMinutes(15)))
                return Results.Json(new { error = "too many attempts, try again later" }, statusCode: 429);

            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { return Results.Json(new { error = "invalid json" }, statusCode: 400); }
            using (doc)
            {
                var username = doc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
                var password = doc.RootElement.TryGetProperty("password", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return Results.Json(new { error = "username and password required" }, statusCode: 400);

                if (services.Users is null || !services.Users.Authenticate(username, password, out var account))
                    return Results.Json(new { error = "invalid credentials" }, statusCode: 401);

                var token = McpServer.GenerateToken(account!.Username, account.Role);
                return Results.Json(new { token, username = account.Username, role = account.Role, expiresIn = 86400, mustChangePassword = account.MustChangePassword });
            }
        });

        api.MapGet("/auth/verify", (ClaimsPrincipal user) =>
        {
            var username = user.Identity?.Name;
            var role = user.FindFirstValue(ClaimTypes.Role) ?? "user";
            return Results.Json(new { ok = true, username, role });
        });

        api.MapGet("/users", [Authorize(Roles = "admin")] () =>
        {
            if (services.Users is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            return Results.Json(services.Users.GetAll().Select(u => new
            {
                username = u.Username,
                role = u.Role,
                createdAtUtc = u.CreatedAtUtc,
                mustChangePassword = u.MustChangePassword
            }));
        });

        api.MapPost("/users", [Authorize(Roles = "admin")] async (JsonElement body) =>
        {
            if (services.Users is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var username = body.TryGetProperty("username", out var u) ? u.GetString() : null;
            var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
            var role = body.TryGetProperty("role", out var r) ? r.GetString() : "user";
            if (!services.Users.CreateUser(username ?? "", password ?? "", role ?? "user", out var error))
                return Results.Json(new { error }, statusCode: 400);
            return Results.Json(new { ok = true, username, role = role ?? "user" });
        });

        api.MapPost("/users/role", [Authorize(Roles = "admin")] (JsonElement body) =>
        {
            if (services.Users is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var username = body.TryGetProperty("username", out var u) ? u.GetString() : null;
            var role = body.TryGetProperty("role", out var r) ? r.GetString() : null;
            if (!services.Users.UpdateRole(username ?? "", role ?? "", out var error))
                return Results.Json(new { error }, statusCode: 400);
            return Results.Json(new { ok = true, username, role });
        });

        api.MapPost("/users/delete", [Authorize(Roles = "admin")] (JsonElement body) =>
        {
            if (services.Users is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var username = body.TryGetProperty("username", out var u) ? u.GetString() : null;
            if (string.Equals(username, RequestContext.Current.Value, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Нельзя удалить собственную учётную запись." }, statusCode: 400);
            if (!services.Users.Delete(username ?? "", out var error))
                return Results.Json(new { error }, statusCode: 400);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/users/password", (JsonElement body) =>
        {
            if (services.Users is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var username = RequestContext.Current.Value;
            var current = body.TryGetProperty("current", out var c) ? c.GetString() : null;
            var next = body.TryGetProperty("next", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(username))
                return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            if (!services.Users.ChangePassword(username, current ?? "", next ?? "", out var error))
                return Results.Json(new { error }, statusCode: 400);
            return Results.Json(new { ok = true });
        });

        api.MapGet("/traffic/log", (int? count) =>
        {
            if (services.Sniffer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var records = services.Sniffer.GetRecent(count ?? 500);
            return Results.Json(records.Select(r => new
            {
                time = r.Timestamp.ToString("HH:mm:ss"),
                src = r.SrcIp, dst = r.DstIp,
                proto = r.Protocol, sport = r.SrcPort, dport = r.DstPort,
                len = r.Length, ttl = r.Ttl,
                raw = r.Raw is null ? null : Convert.ToHexString(r.Raw),
                msg = r.Summary
            }));
        });

        api.MapGet("/traffic/export", (string format) =>
        {
            if (services.Sniffer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var records = services.Sniffer.GetAll();

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("time,src,dst,proto,sport,dport,ttl,len,info");
                foreach (var r in records)
                {
                    sb.Append('"').Append(r.Timestamp.ToString("O")).Append("\",")
                      .Append('"').Append(r.SrcIp).Append("\",")
                      .Append('"').Append(r.DstIp).Append("\",")
                      .Append('"').Append(r.Protocol).Append("\",")
                      .Append(r.SrcPort).Append(',')
                      .Append(r.DstPort).Append(',')
                      .Append(r.Ttl).Append(',')
                      .Append(r.Length).Append(',')
                      .Append('"').Append((r.Summary ?? "").Replace("\"", "\"\"")).AppendLine("\"");
                }
                var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                return Results.File(csv, "text/csv; charset=utf-8", "traffic.csv");
            }

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(0xa1b2c3d4);
                bw.Write((ushort)2);
                bw.Write((ushort)4);
                bw.Write(0);
                bw.Write(0);
                bw.Write(65535u);
                bw.Write(101u);
                foreach (var r in records)
                {
                    if (r.Raw is null) continue;
                    bw.Write((uint)new DateTimeOffset(r.Timestamp).ToUnixTimeSeconds());
                    bw.Write((uint)(r.Timestamp.Millisecond * 1000));
                    bw.Write((uint)r.Raw.Length);
                    bw.Write((uint)r.Length);
                    bw.Write(r.Raw);
                }
            }
            return Results.File(ms.ToArray(), "application/vnd.tcpdump.pcap", "traffic.pcap");
        });

        api.MapGet("/traffic/stats", () =>
        {
            if (services.Sniffer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            return Results.Json(services.Sniffer.GetStats());
        });

        api.MapGet("/traffic/patterns", () =>
        {
            if (services.Analyzer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var patterns = services.Analyzer.GetPatterns();
            return Results.Json(patterns.Select(p => new
            {
                id = p.Id, description = p.Description,
                related_ips = p.RelatedIps ?? [], suggested_action = p.SuggestedAction,
                detected_at = p.DetectedAt.ToString("O")
            }));
        });

        api.MapPost("/traffic/clear", () =>
        {
            if (services.Sniffer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            services.Sniffer.Clear();
            services.Analyzer?.ClearPatterns();
            return Results.Json(new { ok = true });
        });

        api.MapPost("/traffic/patterns/clear", () =>
        {
            if (services.Analyzer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            services.Analyzer.ClearPatterns();
            return Results.Json(new { ok = true });
        });

        api.MapPost("/traffic/test-data", () =>
        {
            if (services.Sniffer is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            GenerateTestData(services);
            return Results.Json(new { ok = true, count = 20 });
        });

        api.MapGet("/traffic/analysis/instant", async () =>
        {
            if (services.ReportService is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var report = await services.ReportService.GenerateInstantReportAsync();
            return Results.Json(report);
        });

        api.MapGet("/traffic/analysis/daily", async () =>
        {
            if (services.ReportService is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var report = await services.ReportService.GenerateDailyReportAsync();
            return Results.Json(report);
        });

        api.MapGet("/traffic/analysis/weekly", async () =>
        {
            if (services.ReportService is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var report = await services.ReportService.GenerateWeeklyReportAsync();
            return Results.Json(report);
        });

        api.MapPost("/traffic/explain", async (JsonElement body) =>
        {
            if (services.LlmClient is null) return Results.Json(new { error = "not ready" }, statusCode: 503);
            var explainType = body.TryGetProperty("type", out var tEl) ? tEl.GetString() : "pattern";
            var context = body.TryGetProperty("context", out var cEl) ? cEl.GetString() : "";
            var relatedIps = body.TryGetProperty("related_ips", out var ripEl) && ripEl.ValueKind == JsonValueKind.Array
                ? string.Join(", ", ripEl.EnumerateArray().Select(x => x.GetString() ?? ""))
                : "";
            var knownIps = services.IpRegistry?.GetAll() ?? [];
            var ipNames = string.Join(", ", knownIps.Select(i => $"{i.Ip}={i.Name}"));
            var prompt = $"Ты — эксперт по сетевому трафику DMX/OSC.\n" +
                $"Объясни простым языком что это такое, чем это может быть опасно, и что с этим делать.\n\n" +
                $"Тип: {explainType}\nОписание: {context}\nСвязанные IP: {relatedIps}\nИзвестные устройства: {ipNames}\n\n" +
                $"Ответь кратко (3-5 предложений) на русском языке.";
            var explanation = await services.LlmClient.GenerateAsync(prompt, new LlmRequestOptions(
                Temperature: 0.3, NumPredict: 256));
            return Results.Json(new { explanation });
        });

        api.MapGet("/vulnerabilities", () =>
        {
            if (VulnerabilityCache.LastCheck is null)
                return Results.Json(new
                {
                    @checked = false,
                    hasVulnerabilities = false,
                    details = "Проверка ещё не выполнялась",
                    lastCheck = (string?)null
                });

            return Results.Json(new
            {
                @checked = true,
                hasVulnerabilities = VulnerabilityCache.HasVulnerabilities,
                details = VulnerabilityCache.Details ?? "Проблем не найдено",
                lastCheck = VulnerabilityCache.LastCheck.Value.ToString("yyyy-MM-dd HH:mm:ss")
            });
        });

        api.MapGet("/osc/status", () =>
        {
            if (services.OscServer is null) return Results.Json(new { error = "osc server not ready" }, statusCode: 503);
            var last = services.OscServer.LastReceived;
            return Results.Json(new
            {
                lastReceived = last is null ? null : new
                {
                    path = last.Path, value = last.Value, from = last.SenderIp,
                    time = last.Timestamp.ToString("HH:mm:ss")
                }
            });
        });

        api.MapPost("/artnet/poll", async () =>
        {
            if (services.Artnet is null) return Results.Json(new { error = "artnet not ready" }, statusCode: 503);
            if (services.Sniffer is null) return Results.Json(new { error = "sniffer not ready" }, statusCode: 503);

            var ips = await services.Artnet.SendPollAsync(services.Sniffer);
            if (ips.Count == 0)
            {
                var fakeDevices = SimulatedIps.Select(ip => new
                {
                    ip,
                    name = services.IpRegistry?.Lookup(ip) ?? "Unknown Device"
                }).ToArray();
                return Results.Json(new { devices = fakeDevices });
            }

            var devices = ips.Select(ip => new
            {
                ip,
                name = services.IpRegistry?.Lookup(ip) ?? "Unknown Device"
            });
            return Results.Json(new { devices });
        });

        api.MapPost("/test/device", async (JsonElement body) =>
        {
            var testIp = body.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
            var testList = body.TryGetProperty("tests", out var testsEl) && testsEl.ValueKind == JsonValueKind.Array
                ? testsEl.EnumerateArray().Select(t => t.GetString()).Where(t => t != null).Cast<string>().ToList()
                : new List<string>();

            if (string.IsNullOrWhiteSpace(testIp))
                return Results.Json(new { error = "ip required" }, statusCode: 400);
            if (!System.Net.IPAddress.TryParse(testIp, out _))
                return Results.Json(new { error = "invalid ip address" }, statusCode: 400);

            var results = new List<object>();

            if (testList.Contains("ping"))
            {
                try
                {
                    if (SimulatedIps.Contains(testIp))
                        results.Add(new { test = "ping", success = true, details = new { ms = 0, error = (string?)null } });
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
                        results.Add(new { test = "http", success = true, details = new { statusCode = 200, error = (string?)null } });
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
                    if (services.Osc is null) results.Add(new { test = "osc", success = false, details = new { command = "/ping", error = "osc not ready" } });
                    else if (services.OscServer is null) results.Add(new { test = "osc", success = false, details = new { command = "/ping", error = "osc server not ready" } });
                    else
                    {
                        var (ok, detail) = await services.Osc.SendAndVerifyAsync("/ping", testIp, services.OscServer);
                        results.Add(new { test = "osc", success = ok, details = new { command = "/ping", error = ok ? (string?)null : detail, detail = ok ? detail : (string?)null } });
                    }
                }
                catch (Exception ex) { results.Add(new { test = "osc", success = false, details = new { command = "/ping", error = ex.Message } }); }
            }

            if (testList.Contains("artnet"))
            {
                try
                {
                    if (services.Artnet is null) results.Add(new { test = "artnet", success = false, details = new { universe = 0, channel = 1, value = 255, error = "artnet not ready" } });
                    else if (SimulatedIps.Contains(testIp))
                        results.Add(new { test = "artnet", success = true, details = new { universe = 0, channel = 1, value = 255, error = (string?)null } });
                    else
                    {
                        await services.Artnet.SendBroadcastAsync(0, 1, 255);
                        _ = Task.Run(async () => { await Task.Delay(1000); await services.Artnet.SendBroadcastAsync(0, 1, 0); });
                        results.Add(new { test = "artnet", success = true, details = new { universe = 0, channel = 1, value = 255, error = (string?)null } });
                    }
                }
                catch (Exception ex) { results.Add(new { test = "artnet", success = false, details = new { universe = 0, channel = 1, value = 255, error = ex.Message } }); }
            }

            return Results.Json(new { ip = testIp, results });
        });

        api.MapPost("/voice/transcribe", async (HttpRequest request) =>
        {
            if (services.Whisper is null)
                return Results.Json(new { error = "whisper not ready" }, statusCode: 500);

            try
            {
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms);
                var audioData = ms.ToArray();

                if (audioData.Length == 0)
                    return Results.Json(new { error = "empty audio" }, statusCode: 400);

                var user = RequestContext.Current.Value ?? "";
                var speech = services.Users?.GetLlmProfile(user).Speech ?? new SpeechSelection();
                if (speech.Provider != "api") await services.Whisper.EnsureModelAsync();
                var text = await services.Whisper.TranscribeAsync(audioData, speech.Provider, speech.Model, speech.Lang);
                return Results.Json(new { text = text ?? "", ok = text != null });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message, ok = false }, statusCode: 500);
            }
        });

        api.MapPost("/voice/wake", [AllowAnonymous] async (HttpRequest request) =>
        {
            var clientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!RateLimiter.Allow($"wake:{clientIp}", 60, TimeSpan.FromMinutes(1)))
                return Results.Json(new { isHotword = false, score = 0f, threshold = 0f, durationSec = 0f, frames = 0 });

            try
            {
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms);
                var audioData = ms.ToArray();

                if (audioData.Length == 0 || services.WakeWord is null)
                    return Results.Json(new { isHotword = false, score = 0f, threshold = 0f, durationSec = 0f, frames = 0 });

                var result = services.WakeWord.Check(audioData);
                return Results.Json(new
                {
                    isHotword = result.IsHotword,
                    score = Math.Round(result.Score, 3),
                    threshold = Math.Round(result.Threshold, 3),
                    durationSec = Math.Round(result.DurationSec, 3),
                    frames = result.Frames
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WAKE] Error: {ex.Message}");
                return Results.Json(new { isHotword = false, score = 0f, threshold = 0f, durationSec = 0f, frames = 0 });
            }
        });

        api.MapPost("/voice/wake/train", async (HttpRequest request) =>
        {
            try
            {
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms);
                var audioData = ms.ToArray();

                if (audioData.Length == 0 || services.WakeWord is null)
                    return Results.Json(new { ok = false, error = "empty audio" }, statusCode: 400);

                var result = services.WakeWord.Train(audioData);
                if (result.Ok)
                    services.OnWakeConfigChanged?.Invoke();
                return Results.Json(new
                {
                    ok = result.Ok,
                    error = result.Error,
                    frames = result.Frames,
                    durationSec = Math.Round(result.DurationSec, 3),
                    templates = result.TemplatesCount
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WAKE] Train error: {ex.Message}");
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
            }
        });

        api.MapGet("/voice/wake/config", () =>
        {
            if (services.WakeWord is null)
                return Results.Json(new { enabled = false, threshold = 0f, trained = false, templates = Array.Empty<object>() });

            var templates = services.WakeWord.Templates;
            return Results.Json(new
            {
                enabled = services.WakeWord.Enabled,
                threshold = Math.Round(services.WakeWord.Threshold, 3),
                trained = templates is { Count: > 0 },
                templates = (templates ?? new List<List<List<float>>>()).Select(t => new
                {
                    frames = t.Count,
                    durationSec = Math.Round(t.Count * WakeDsp.FrameHop / (double)WakeDsp.SampleRate, 2)
                })
            });
        });

        api.MapDelete("/voice/wake", () =>
        {
            if (services.WakeWord is null)
                return Results.Json(new { ok = false, error = "wake not ready" }, statusCode: 503);

            services.WakeWord.ClearTemplates();
            services.OnWakeConfigChanged?.Invoke();
            return Results.Json(new { ok = true });
        });

        api.MapPost("/voice/process", async (JsonElement body) =>
        {
            try
            {
                var text = body.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text))
                    return Results.Json(new { ok = false, response = "text is required" }, statusCode: 400);

                var navResult = DetectNav(text);
                if (navResult is not null)
                    return Results.Json(new { ok = true, response = navResult.Value.response, nav = navResult.Value.nav });

                if (services.OnVoiceProcess is null)
                    return Results.Json(new { ok = false, response = "voice processing not available" }, statusCode: 503);

                var llmResponse = await services.OnVoiceProcess(text);
                return Results.Json(new { ok = true, response = llmResponse ?? "Готово." });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, response = $"Ошибка: {ex.Message}" }, statusCode: 500);
            }
        });

        api.MapPost("/voice/speak", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                JsonDocument reqDoc;
                try { reqDoc = JsonDocument.Parse(body); }
                catch (JsonException)
                {
                    return Results.Json(new { ok = false, error = "invalid json" }, statusCode: 400);
                }
                using (reqDoc)
                {
                    var text = reqDoc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text))
                        return Results.Json(new { ok = false, error = "text is required" }, statusCode: 400);

                    if (services.Tts is null)
                        return Results.Json(new { ok = false, error = "TTS not available" }, statusCode: 503);

                    var audio = await services.Tts.SynthesizeAsync(text);
                    if (audio is null || audio.Length == 0)
                        return Results.Json(new { ok = false, error = "synthesis failed" }, statusCode: 500);

                    return Results.File(audio, "audio/wav");
                }
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
            }
        });

        api.MapGet("/system/stats", () =>
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                var allProcs = Process.GetProcesses()
                    .OrderByDescending(p => { try { return p.TotalProcessorTime.TotalMilliseconds; } catch { return 0.0; } })
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
                var ramPercent = Math.Round(workingSet / (double)(2L * 1024 * 1024 * 1024) * 100, 1);

                return Results.Json(new
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
                Console.Error.WriteLine($"[STATS] {ex}");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        api.MapPost("/admin/tools/reload", [Authorize(Roles = "admin")] () =>
        {
            var registry = McpServer.Registry;
            var sse = McpServer.Sse;
            if (registry is null || sse is null)
                return Results.Json(new { error = "server not ready" }, statusCode: 503);

            var providers = McpServer.ReloadProviders;
            var tools = 0;
            var recreated = providers is not null;
            if (recreated)
            {
                registry.Rebuild(providers!);
                tools = registry.GetAll().Count;
                Console.WriteLine($"[ADMIN] Tools reloaded: {tools} tools, {providers!.Count} providers");
            }

            sse.BroadcastJson(new { type = "tools_changed", timestamp = DateTime.UtcNow });
            return Results.Json(new { ok = true, tools, recreated });
        });

        api.MapGet("/ip-registry", () =>
        {
            if (services.IpRegistry is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);
            return Results.Json(services.IpRegistry.GetAll().Select(m => new { ip = m.Ip, name = m.Name, notes = m.Notes }));
        });

        api.MapGet("/tasks", () =>
        {
            if (services.Tasks is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);
            return Results.Json(services.Tasks.GetAll().Select(t => new
            {
                id = t.Id, name = t.Name, schedule = t.Schedule, desc = t.Description,
                icon = t.Icon, enabled = t.Enabled,
                lastRun = t.LastRun?.ToString("HH:mm:ss"),
                nextRun = t.NextRun?.ToString("HH:mm:ss"),
                lastError = t.LastError
            }));
        });

        api.MapGet("/fs/list", [Authorize(Roles = "admin")] (string? path) =>
        {
            if (services.FileBrowser is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);
            try
            {
                var listing = services.FileBrowser.List(path);
                return Results.Json(new
                {
                    path = listing.Path,
                    parent = listing.Parent,
                    entries = listing.Entries.Select(e => new
                    {
                        name = e.Name,
                        path = e.Path,
                        dir = e.IsDir,
                        size = e.Size,
                        modified = e.Modified?.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 403);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        api.MapGet("/fs/read", [Authorize(Roles = "admin")] (string? path) =>
        {
            if (services.FileBrowser is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);
            try
            {
                var result = services.FileBrowser.Read(path);
                if (result is null)
                    return Results.Json(new { error = "file not found" }, statusCode: 404);
                return Results.Json(new
                {
                    path = path ?? "",
                    size = result.Size,
                    truncated = result.Truncated,
                    binary = result.Binary,
                    content = result.Binary ? null : result.Content
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 403);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        api.MapPost("/tasks/toggle", async (JsonElement body) =>
        {
            if (services.Tasks is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);
            var id = body.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                return Results.Json(new { error = "id required" }, statusCode: 400);
            var ok = services.Tasks.Toggle(id);
            return Results.Json(new { ok });
        });

        api.MapPost("/tasks/run", async (JsonElement body) =>
        {
            if (services.Tasks is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);
            var id = body.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                return Results.Json(new { error = "id required" }, statusCode: 400);
            var ok = services.Tasks.RunNow(id);
            return Results.Json(new { ok });
        });

        api.MapPost("/ip-registry", [Authorize] async (HttpRequest request) =>
        {
            if (services.IpRegistry is null)
                return Results.Json(new { error = "not ready" }, statusCode: 503);

            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { return Results.Json(new { error = "invalid json" }, statusCode: 400); }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("ip", out var ipEl) || !doc.RootElement.TryGetProperty("name", out var nameEl))
                    return Results.Json(new { error = "ip and name required" }, statusCode: 400);

                var ip = ipEl.GetString();
                var name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(name))
                    return Results.Json(new { error = "ip and name must be non-empty" }, statusCode: 400);

                if (!System.Net.IPAddress.TryParse(ip, out _))
                    return Results.Json(new { error = "invalid ip address" }, statusCode: 400);

                services.IpRegistry.Register(ip, name);
                return Results.Json(new { ok = true, ip, name });
            }
        });
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

    private static void GenerateTestData(WebHostServices services)
    {
        if (services.Sniffer is null) return;
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
            services.IpRegistry?.Register(ip, name);

        var artnetGroup = new[]
        {
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=1 val=255 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=2 val=128 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=3 val=200 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=4 val=50 (группировка: соседние каналы 1-5)"),
            (s: "192.168.1.100", d: "10.0.0.2",  p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "DMX ch=5 val=180 (группировка: соседние каналы 1-5)"),
        };
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
        var artnetPoll = new[]
        {
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
        };
        var alertFlood = new[]
        {
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
        };
        var normal = new[]
        {
            (s: "172.16.0.10", d: "192.168.1.100", p: "TCP", sp: 443, dp: 34512, len: 1200, msg: "TCP port 443->34512"),
            (s: "192.168.1.100", d: "8.8.8.8", p: "UDP", sp: 56789, dp: 53, len: 80, msg: null),
            (s: "10.0.0.1", d: "8.8.8.8", p: "ICMP", sp: 0, dp: 0, len: 84, msg: null),
        };

        var offset = 0;
        foreach (var s in artnetGroup)
        {
            services.Sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }
        offset += 5;
        foreach (var s in oscMacro)
        {
            services.Sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }
        offset += 2;
        foreach (var s in artnetPoll)
        {
            services.Sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }
        offset += 2;
        foreach (var s in alertFlood)
        {
            services.Sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }
        offset += 1;
        foreach (var s in normal)
        {
            services.Sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg));
            offset++;
        }
    }
}
