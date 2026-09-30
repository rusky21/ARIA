using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public static class AppHost
{
    /// <summary>
    /// DI-based entry point. All services resolved from the container.
    /// </summary>
    public static async Task RunFromDiAsync(string[] args, IServiceProvider services, CancellationToken shutdownToken = default)
    {
        // ── Resolve all services from DI ──
        var lifetimeManager = new ServiceLifetimeManager();
        var userStore = services.GetRequiredService<UserStore>();
        var chatHistory = services.GetRequiredService<ChatHistoryService>();
        var llmSettings = services.GetRequiredService<LlmSettings>();
        var llmClient = services.GetRequiredService<LlmClient>();
        var ipRegistry = services.GetRequiredService<IpRegistryService>();
        var sniffer = services.GetRequiredService<TrafficSnifferService>();
        var trafficStorage = services.GetRequiredService<TrafficStorageService>();
        var sse = services.GetRequiredService<SseService>();
        var eventBus = services.GetRequiredService<WsEventBus>();
        var analyzer = services.GetRequiredService<TrafficAnalyzerService>();
        var reportService = services.GetRequiredService<TrafficReportService>();
        var artnet = services.GetRequiredService<ArtnetService>();
        var osc = services.GetRequiredService<OscService>();
        var oscServer = services.GetRequiredService<OscServerService>();
        var taskRegistry = services.GetRequiredService<TaskRegistryService>();
        var devService = services.GetRequiredService<DevService>();
        var chatAssistant = services.GetRequiredService<ChatAssistant>();
        var whisperService = services.GetRequiredService<WhisperService>();
        var wakeWord = services.GetRequiredService<WakeWordService>();
        var tts = services.GetRequiredService<TtsService>();
        var ttsSettings = services.GetRequiredService<TtsSettings>();
        var deviceTestService = services.GetRequiredService<DeviceTestService>();
        var deviceDiscovery = services.GetRequiredService<DeviceDiscoveryService>();
        var systemStatsService = services.GetRequiredService<SystemStatsService>();
        var registry = services.GetRequiredService<ToolRegistry>();
        var artNetTools = services.GetRequiredService<ArtNetTools>();
        var oscTools = services.GetRequiredService<OscTools>();
        var networkTools = services.GetRequiredService<NetworkTools>();
        var deviceTools = services.GetRequiredService<DeviceTools>();
        var configTools = services.GetRequiredService<ConfigTools>();
        var adminTools = services.GetRequiredService<AdminTools>();
        var navigationTools = services.GetRequiredService<NavigationTools>();
        var fileBrowser = services.GetRequiredService<FileBrowserService>();
        var visionTools = services.GetRequiredService<VisionTools>();
        var llmRouter = services.GetRequiredService<LlmRouter>();

        // Track disposables for legacy shutdown path
        lifetimeManager.Track(sniffer);
        lifetimeManager.Track(trafficStorage);
        lifetimeManager.Track(oscServer);
        lifetimeManager.Track(taskRegistry);
        lifetimeManager.Track(systemStatsService);

        // ── Startup initialization (storage, devices, sniffer start) ──
        var initializer = services.GetRequiredService<AppInitializer>();
        await initializer.InitializeAsync();

        // ── WebHostServices bag (still needed by McpServer/ApiEndpoints) ──
        var webServices = new WebHostServices
        {
            IpRegistry = ipRegistry,
            Sniffer = sniffer,
            Analyzer = analyzer,
            Artnet = artnet,
            Osc = osc,
            OscServer = oscServer,
            Tasks = taskRegistry,
            FileBrowser = services.GetRequiredService<FileBrowserService>(),
            Users = userStore,
            ChatHistory = chatHistory,
            ReportService = reportService,
            LlmClient = llmClient,
            Whisper = whisperService,
            WakeWord = wakeWord,
            Tts = tts,
        };
        webServices.OnWakeConfigChanged = () =>
            eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);

        var processGate = new SemaphoreSlim(1, 1);
        TaskRouter? router = null;

        async Task OnMessage(string message)
        {
            await processGate.WaitAsync();
            try
            {
                if (router is not null)
                {
                    var result = await router.RouteWithResponseAsync(message);
                    eventBus.TerminalLine(result ?? "Готово.");
                }
            }
            finally
            {
                processGate.Release();
            }
        }

        // ── WS command handler ──
        async Task OnWsCommand(JsonElement cmd)
        {
            var type = cmd.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null) return;

            var wsUser = RequestContext.Current.Value;
            var isAdmin = UserStore.Current?.IsAdmin(wsUser) ?? false;

            Log.Info($"[WS] ← '{wsUser ?? "unknown"}' {type}{DescribeWsAction(type, cmd)}");

            static string DescribeWsAction(string actionType, JsonElement cmd)
            {
                string? Str(string name) =>
                    cmd.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                        ? el.GetString()
                        : null;

                string Raw(string name) =>
                    cmd.TryGetProperty(name, out var el) ? el.GetRawText() : "?";

                return actionType switch
                {
                    "chat" or "voice:process" when Str("text") is { } v => $": \"{Log.Truncate(v)}\"",
                    "terminal:exec" when Str("command") is { } c => $": \"{Log.Truncate(c)}\"",
                    "dev:chat" when Str("text") is { } d => $": \"{Log.Truncate(d)}\" agent={Str("agent")} model={Str("model")}",
                    "traffic:interface:set" or "device:test" when Str("ip") is { } ip => $": ip={ip}",
                    "tasks:toggle" or "tasks:run" when Str("id") is { } id => $": id={id}",
                    "settings:llm:set" => $": provider={Str("provider") ?? "?"} model={(cmd.TryGetProperty("api", out var apiEl) && apiEl.TryGetProperty("model", out var am) ? am.GetString() : (cmd.TryGetProperty("local", out var locEl) && locEl.TryGetProperty("model", out var lm) ? lm.GetString() : null))}",
                    "wake:set" => $": enabled={Raw("enabled")}",
                    "wake:threshold" => $": threshold={Raw("threshold")}",
                    _ => ""
                };
            }

            static string? ExtractFirstImage(JsonElement cmd)
            {
                if (!cmd.TryGetProperty("images", out var images)) return null;
                if (images.ValueKind == JsonValueKind.String)
                {
                    var s = images.GetString();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                }
                if (images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in images.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            return item.GetString();
                    }
                }
                return null;
            }

            if (type.StartsWith("dev:", StringComparison.OrdinalIgnoreCase) && !isAdmin)
            {
                Console.WriteLine($"[WS] dev:* denied for '{wsUser}'");
                return;
            }

            switch (type)
            {
                case "artnet:poll":
                {
                    var ips = await artnet.SendPollAsync(sniffer);
                    var devices = ips.Count > 0
                        ? ips.Select(ip => (ip, ipRegistry.Lookup(ip) ?? "Unknown Device")).ToList()
                        : TestDataService.GetSimulatedDevices(ipRegistry);
                    eventBus.ArtPollResult(devices);
                    break;
                }

                case "device:test":
                {
                    var testIp = cmd.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                    var tests = cmd.TryGetProperty("tests", out var testsEl)
                        ? testsEl.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToList()
                        : new List<string>();
                    if (string.IsNullOrWhiteSpace(testIp)) break;
                    var results = await deviceTestService.RunTestsAsync(testIp, tests);
                    eventBus.DeviceTestResult(testIp, results);
                    break;
                }

                case "device:scan":
                {
                    if (deviceDiscovery.IsScanning) break;
                    eventBus.DevicesScanning(true);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await deviceDiscovery.ScanAsync();
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[DEVICE-DISCOVERY] Scan failed: {ex.Message}");
                        }
                        finally
                        {
                            eventBus.DevicesScanning(false);
                        }
                    });
                    break;
                }

                case "camera:open":
                {
                    var cameraTarget = cmd.TryGetProperty("ip", out var camEl) ? camEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(cameraTarget)) break;
                    var cameraIp = deviceDiscovery.ResolveTarget(cameraTarget) ?? cameraTarget;
                    var cameraLabel = deviceDiscovery.CameraLabel(cameraTarget, cameraIp);
                    var cameraUrl = deviceDiscovery.BuildCameraMjpegUrl(cameraIp);
                    eventBus.UIWindowOpen("camera", cameraUrl, cameraLabel);
                    break;
                }

                case "camera:snapshot":
                {
                    var snapTarget = cmd.TryGetProperty("ip", out var snapEl) ? snapEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(snapTarget)) break;

                    var snapIp = deviceDiscovery.ResolveTarget(snapTarget) ?? snapTarget;
                    if (cmd.TryGetProperty("user", out var snapUserEl) && cmd.TryGetProperty("pass", out var snapPassEl))
                    {
                        var snapUser = snapUserEl.GetString();
                        var snapPass = snapPassEl.GetString();
                        if (!string.IsNullOrWhiteSpace(snapUser))
                            deviceDiscovery.SetCameraCredentials(snapIp, snapUser, snapPass);
                    }

                    var snapResult = await visionTools.GetSnapshotAsync(snapTarget);
                    if (snapResult.Bytes is not null && snapResult.Bytes.Length > 0)
                        eventBus.CameraSnapshot(snapTarget, true, Convert.ToBase64String(snapResult.Bytes));
                    else
                        eventBus.CameraSnapshot(snapTarget, false, "", snapResult.Error ?? "Не удалось получить снимок.");
                    break;
                }

                case "camera:analyze":
                {
                    var camTarget = cmd.TryGetProperty("ip", out var camEl2) ? camEl2.GetString() : null;
                    if (string.IsNullOrWhiteSpace(camTarget)) break;
                    var analyzeResult = await visionTools.AnalyzeCamera(camTarget, null, wsUser ?? "");
                    var analyzeOk = !analyzeResult.StartsWith("Ошибка", StringComparison.Ordinal)
                        && !analyzeResult.StartsWith("Не удалось", StringComparison.Ordinal);
                    eventBus.CameraAnalyze(camTarget, analyzeOk, analyzeResult);
                    break;
                }

                case "voice:process":
                {
                    var text = cmd.TryGetProperty("text", out var txtEl) ? txtEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text)) break;

                    var voiceUser = wsUser ?? "unknown";
                    var voiceHistory = chatHistory.GetHistory(voiceUser);
                    await processGate.WaitAsync();
                    string? response = null;
                    try
                    {
                        if (router is not null)
                        {
                            Log.Info($"[AI] → запрос отправлен ИИ (модель: {llmClient.CurrentModel}): \"{Log.Truncate(text)}\"");
                            response = await router.RouteWithResponseAsync(text, voiceHistory, username: voiceUser, chatTask: LlmTask.Voice);
                        }
                        else
                        {
                            Log.Info("[AI] ✗ НЕ отправлено: ядро ещё не готово");
                        }
                    }
                    finally { processGate.Release(); }

                    chatHistory.Add(voiceUser, "user", text);
                    chatHistory.Add(voiceUser, "assistant", response ?? "Готово.");
                    eventBus.VoiceResponse(response ?? "Готово.");
                    break;
                }

                case "voice:speak":
                {
                    var text = cmd.TryGetProperty("text", out var txtEl2) ? txtEl2.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text) || tts is null)
                    {
                        eventBus.VoiceSpeakDone(false, "Синтез речи недоступен");
                        break;
                    }
                    var rate = cmd.TryGetProperty("rate", out var rateEl) && rateEl.TryGetSingle(out var r) ? r : 1f;
                    byte[]? wav;
                    if (string.Equals(ttsSettings.Engine, "api", StringComparison.OrdinalIgnoreCase))
                    {
                        wav = await tts.SynthesizeViaApiAsync(text, ttsSettings.Voice, rate);
                    }
                    else
                    {
                        wav = await tts.SynthesizeAsync(text, rate);
                    }
                    if (wav is not null && wav.Length > 0)
                    {
                        eventBus.VoiceAudio(wav);
                        eventBus.VoiceSpeakDone(true, null);
                    }
                    else
                    {
                        eventBus.VoiceSpeakDone(false, "Синтез речи недоступен");
                    }
                    break;
                }

                case "wake:set":
                {
                    if (cmd.TryGetProperty("enabled", out var enabledEl) && wakeWord is not null)
                    {
                        wakeWord.SetEnabled(enabledEl.GetBoolean());
                        eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                    }
                    break;
                }

                case "wake:threshold":
                {
                    if (cmd.TryGetProperty("threshold", out var thEl) && thEl.TryGetSingle(out var thr) && wakeWord is not null)
                    {
                        wakeWord.SetThreshold(thr);
                        eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                    }
                    break;
                }

                case "terminal:exec":
                {
                    var command = cmd.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(command)) break;
                    await processGate.WaitAsync();
                    try
                    {
                        if (router is not null)
                        {
                            Log.Info($"[AI] → запрос отправлен ИИ (модель: {llmClient.CurrentModel}): \"{Log.Truncate(command)}\"");
                            var result = await router.RouteWithResponseAsync(command);
                            eventBus.TerminalLine(result ?? "Готово.");
                        }
                        else
                        {
                            Log.Info("[AI] ✗ НЕ отправлено: ядро ещё не готово");
                            eventBus.TerminalLine("Ядро ещё не готово, попробуйте чуть позже.");
                        }
                    }
                    finally { processGate.Release(); }
                    break;
                }

                case "traffic:clear":
                {
                    sniffer.Clear();
                    analyzer.ClearPatterns();
                    eventBus.TrafficCleared();
                    break;
                }

                case "traffic:capture_stop":
                {
                    sniffer.Stop();
                    Log.Info($"[WS] ← '{wsUser ?? "unknown"}' захват трафика ОСТАНОВЛЕН");
                    eventBus.TrafficCaptureState(false, (ulong)sniffer.TotalCaptured);
                    break;
                }

                case "traffic:capture_start":
                {
                    sniffer.Start();
                    Log.Info($"[WS] ← '{wsUser ?? "unknown"}' захват трафика ЗАПУЩЕН");
                    eventBus.TrafficCaptureState(true, (ulong)sniffer.TotalCaptured);
                    break;
                }

                case "traffic:interfaces":
                {
                    eventBus.NetworkInterfaces(sniffer.ListInterfaces(), sniffer.SelectedIp);
                    break;
                }

                case "traffic:interface:set":
                {
                    var ifIp = cmd.TryGetProperty("ip", out var ifIpEl) ? ifIpEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(ifIp)) break;

                    var (ok, error) = sniffer.SetInterface(ifIp);
                    if (ok)
                    {
                        sniffer.Clear();
                        eventBus.TrafficCleared();
                    }
                    eventBus.InterfaceSetResult(ok, ifIp, ok ? null : error);
                    eventBus.NetworkInterfaces(sniffer.ListInterfaces(), sniffer.SelectedIp);
                    break;
                }

                case "traffic:patterns_clear":
                {
                    analyzer.ClearPatterns();
                    eventBus.PatternsCleared();
                    break;
                }

                case "chat":
                {
                    var chatText = cmd.TryGetProperty("text", out var chatEl) ? chatEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(chatText)) break;
                    var historyUser = wsUser ?? "unknown";
                    var convId = cmd.TryGetProperty("conversationId", out var cidEl) && cidEl.ValueKind == JsonValueKind.String
                        ? cidEl.GetString() : chatHistory.GetLastConversationId(historyUser);
                    if (convId is null)
                    {
                        var newMeta = chatHistory.CreateConversation(historyUser);
                        convId = newMeta.Id;
                        eventBus.ChatCreated(newMeta);
                    }
                    var historyForLlm = chatHistory.GetHistory(historyUser, convId);
                    var imageDataUrl = ExtractFirstImage(cmd);
                    chatHistory.Add(historyUser, convId, "user", chatText, imageDataUrl);
                    await processGate.WaitAsync();
                    try
                    {
                        if (router is not null)
                        {
                            if (imageDataUrl is not null)
                            {
                                Log.Info($"[AI] → запрос отправлен ИИ (chat + изображение): \"{Log.Truncate(chatText)}\"");
                                var (_, chatOpts, _) = await llmRouter.ResolveAsync(LlmTask.Vision, historyUser);
                                eventBus.ChatThinking("Анализирую запрос…");
                                var sb = new System.Text.StringBuilder();
                                await foreach (var chunk in chatAssistant.AskStreamingAsync(chatText, historyForLlm, chatOpts, imageDataUrl))
                                {
                                    sb.Append(chunk);
                                    eventBus.ChatReasoning(chunk);
                                }
                                var answer = sb.ToString().Trim();
                                if (string.IsNullOrWhiteSpace(answer)) answer = "Не удалось получить ответ.";
                                chatHistory.Add(historyUser, convId, "assistant", answer);
                                eventBus.Chat(answer, done: true);
                            }
                            else
                            {
                                Log.Info($"[AI] → запрос отправлен ИИ (модель: {llmClient.CurrentModel}): \"{Log.Truncate(chatText)}\"");
                                eventBus.ChatThinking("Анализирую запрос…");
                                var answer = await router.RouteWithStreamingAsync(chatText, historyForLlm, async chunk => eventBus.ChatReasoning(chunk), historyUser);
                                chatHistory.Add(historyUser, convId, "assistant", answer ?? "Готово.");
                                eventBus.Chat(answer ?? "Готово.", done: true);
                            }
                        }
                        else
                        {
                            Log.Info("[AI] ✗ НЕ отправлено: ядро ещё не готово");
                            eventBus.Chat("Ядро ещё не готово, попробуйте чуть позже.", done: true);
                        }
                    }
                    finally { processGate.Release(); }
                    break;
                }

                case "chat:clear":
                {
                    var clearConvId = cmd.TryGetProperty("conversationId", out var ccidEl) && ccidEl.ValueKind == JsonValueKind.String
                        ? ccidEl.GetString() : chatHistory.GetLastConversationId(wsUser ?? "");
                    if (clearConvId is not null)
                        chatHistory.Reset(wsUser ?? "", clearConvId);
                    eventBus.ChatHistoryCleared();
                    break;
                }

                case "chat:create":
                {
                    var title = cmd.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                        ? titleEl.GetString() : null;
                    var meta = chatHistory.CreateConversation(wsUser ?? "unknown", title);
                    eventBus.ChatCreated(meta);
                    break;
                }

                case "chat:list":
                {
                    eventBus.ChatList(chatHistory.ListConversations(wsUser ?? "unknown"));
                    break;
                }

                case "chat:switch":
                {
                    var switchConvId = cmd.TryGetProperty("conversationId", out var scidEl) && scidEl.ValueKind == JsonValueKind.String
                        ? scidEl.GetString() : null;
                    if (switchConvId is not null)
                    {
                        var msgs = chatHistory.GetHistory(wsUser ?? "unknown", switchConvId);
                        eventBus.ChatSwitched(switchConvId, msgs);
                    }
                    break;
                }

                case "chat:delete":
                {
                    var delConvId = cmd.TryGetProperty("conversationId", out var dcidEl) && dcidEl.ValueKind == JsonValueKind.String
                        ? dcidEl.GetString() : null;
                    if (delConvId is not null)
                    {
                        chatHistory.DeleteConversation(wsUser ?? "unknown", delConvId);
                        eventBus.ChatDeleted(delConvId);
                    }
                    break;
                }

                case "chat:rename":
                {
                    var renameConvId = cmd.TryGetProperty("conversationId", out var rcidEl) && rcidEl.ValueKind == JsonValueKind.String
                        ? rcidEl.GetString() : null;
                    var newTitle = cmd.TryGetProperty("title", out var rtEl) && rtEl.ValueKind == JsonValueKind.String
                        ? rtEl.GetString() : null;
                    if (renameConvId is not null && newTitle is not null)
                    {
                        chatHistory.RenameConversation(wsUser ?? "unknown", renameConvId, newTitle);
                        eventBus.ChatRenamed(renameConvId, newTitle);
                    }
                    break;
                }

                case "traffic:patterns":
                {
                    var pats = analyzer.GetPatterns();
                    eventBus.Patterns(pats);
                    break;
                }

                case "traffic:test-data":
                {
                    TestDataService.GenerateTestData(sniffer, ipRegistry);
                    eventBus.TrafficLog(sniffer.GetRecent(500));
                    break;
                }

                case "traffic:analysis:instant":
                {
                    var report = await reportService.GenerateInstantReportAsync();
                    eventBus.TrafficReport(report);
                    break;
                }

                case "traffic:analysis:daily":
                {
                    RunSafe.Run(async () =>
                    {
                        var report = await reportService.GenerateDailyReportAsync();
                        eventBus.TrafficReport(report);
                    }, "Report.Daily");
                    break;
                }

                case "traffic:analysis:weekly":
                {
                    RunSafe.Run(async () =>
                    {
                        var report = await reportService.GenerateWeeklyReportAsync();
                        eventBus.TrafficReport(report);
                    }, "Report.Weekly");
                    break;
                }

                case "traffic:explain":
                {
                    var explainType = cmd.TryGetProperty("explain_type", out var etEl) ? etEl.GetString() : "pattern";
                    var targetId = cmd.TryGetProperty("id", out var idEl2) ? idEl2.GetString() : "";
                    var context = cmd.TryGetProperty("context", out var ctxEl) ? ctxEl.GetString() : "";
                    var relatedIpsEl = cmd.TryGetProperty("related_ips", out var ripEl) && ripEl.ValueKind == JsonValueKind.Array
                        ? ripEl.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                        : [];

                    RunSafe.Run(async () =>
                    {
                        var ipsJson = string.Join(", ", relatedIpsEl);
                        var knownIps = ipRegistry.GetAll();
                        var ipNames = string.Join(", ", knownIps.Select(i => $"{i.Ip}={i.Name}"));

                        var typeLabel = explainType switch
                        {
                            "anomaly" => "аномалию в сетевом трафике",
                            "command_pattern" => "паттерн команд в сетевом трафике",
                            _ => "паттерн/событие в сетевом трафике"
                        };

                        var prompt = $"Проанализируй {typeLabel} и дай конкретный ответ по делу.\n\n" +
                            $"ДАННЫЕ:\n{context}\n\n" +
                            $"Связанные IP: {ipsJson}\n" +
                            $"Известные устройства в сети: {ipNames}\n\n" +
                            $"Требования:\n" +
                            $"- Объясни ЧТО именно произошло (конкретно для этих данных, а не вообще)\n" +
                            $"- Какие конкретно риски это создаёт для данной сети\n" +
                            $"- Конкретные рекомендации что делать (не общие слова)\n" +
                            $"- Если есть IP-адреса — скажи какое устройство под ними и что оно делает\n" +
                            $"- Ответь на русском, 4-8 предложений, без воды";

                        var explanation = await llmClient.GenerateAsync(prompt, new LlmRequestOptions(
                            Temperature: 0.3, NumPredict: 512));

                        eventBus.TrafficExplanation(targetId ?? "", explanation ?? "Не удалось получить объяснение.");
                    }, "Report.Explain");
                    break;
                }

                case "tasks:list":
                {
                    eventBus.Tasks(taskRegistry.GetAll());
                    break;
                }

                case "tasks:toggle":
                {
                    var taskId = cmd.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(taskId))
                        taskRegistry.Toggle(taskId);
                    eventBus.Tasks(taskRegistry.GetAll());
                    break;
                }

                case "tasks:run":
                {
                    var taskId = cmd.TryGetProperty("id", out var runEl) ? runEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(taskId))
                        taskRegistry.RunNow(taskId);
                    eventBus.Tasks(taskRegistry.GetAll());
                    break;
                }

                case "dev:auth":
                {
                    var password = cmd.TryGetProperty("password", out var pwEl) ? pwEl.GetString() : null;
                    var ok = !string.IsNullOrEmpty(password) && devService.CheckPassword(password);
                    Console.WriteLine($"[DEV] Auth: {(ok ? "ok" : "failed")}");
                    eventBus.DevAuthResult(ok, devService.CanChangePassword());
                    if (ok)
                        eventBus.DevHistory(devService.GetHistory());
                    break;
                }

                case "dev:options":
                {
                    RunSafe.Run(async () =>
                    {
                        var (agents, models) = await devService.GetOptionsAsync();
                        eventBus.DevOptions(agents, models);
                    }, "Dev.Options");
                    break;
                }

                case "dev:change-password":
                {
                    var current = cmd.TryGetProperty("current", out var curEl) ? curEl.GetString() : null;
                    var next = cmd.TryGetProperty("next", out var nextEl) ? nextEl.GetString() : null;
                    if (devService.TryChangePassword(current ?? "", next ?? "", out var err))
                    {
                        eventBus.DevResponse("Пароль изменён.");
                        eventBus.DevDone();
                    }
                    else
                    {
                        eventBus.DevError(err);
                        eventBus.DevDone();
                    }
                    break;
                }

                case "dev:chat":
                {
                    var text = cmd.TryGetProperty("text", out var txtEl) ? txtEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text)) break;
                    var agent = cmd.TryGetProperty("agent", out var agEl) ? agEl.GetString() : null;
                    var model = cmd.TryGetProperty("model", out var mdEl) ? mdEl.GetString() : null;
                    Console.WriteLine($"[DEV] Chat cmd: agent={agent ?? "(default)"} model={model ?? "(default)"} len={text.Length}");
                    RunSafe.Run(() => devService.SendPromptAsync(text, agent, model), "Dev.Chat");
                    break;
                }

                case "dev:apply":
                {
                    if (cmd.TryGetProperty("files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
                    {
                        var changes = filesEl.Deserialize<DevFileChange[]>(DevService.JsonOptions);
                        Console.WriteLine($"[DEV] Apply cmd: {changes?.Length ?? 0} files");
                        if (changes is not null && changes.Length > 0)
                            RunSafe.Run(() => devService.ApplyChangesAsync(changes), "Dev.Apply");
                    }
                    break;
                }

                case "dev:reject":
                {
                    Console.WriteLine("[DEV] Reject cmd");
                    RunSafe.Run(devService.RejectChangesAsync, "Dev.Reject");
                    break;
                }

                case "dev:new-chat":
                {
                    Console.WriteLine("[DEV] New chat cmd");
                    devService.ResetHistory();
                    eventBus.DevResponse("Новый чат. Контекст очищен.");
                    eventBus.DevDone();
                    break;
                }

                case "settings:llm:get":
                {
                    eventBus.LlmSettings(llmSettings);
                    RunSafe.Run(() => PingLlmAndPushAsync(), "PingLlm");
                    break;
                }

                case "settings:llm:set":
                {
                    var provider = cmd.TryGetProperty("provider", out var p) ? p.GetString() : null;
                    if (provider is not ("local" or "api"))
                    {
                        eventBus.LlmSettingsSet(false, "Неизвестный провайдер");
                        break;
                    }

                    if (isAdmin)
                    {
                        if (cmd.TryGetProperty("local", out var loc))
                        {
                            if (loc.TryGetProperty("url", out var u) && u.GetString() is { } uu && !string.IsNullOrWhiteSpace(uu))
                                llmSettings.Local.Url = uu.Trim();
                            if (loc.TryGetProperty("model", out var m) && m.GetString() is { } mm && !string.IsNullOrWhiteSpace(mm))
                                llmSettings.Local.Model = mm.Trim();
                        }
                        if (cmd.TryGetProperty("api", out var ap))
                        {
                            if (ap.TryGetProperty("baseUrl", out var b) && b.GetString() is { } bb && !string.IsNullOrWhiteSpace(bb))
                                llmSettings.Api.BaseUrl = bb.Trim();
                            if (ap.TryGetProperty("model", out var am) && am.GetString() is { } amm && !string.IsNullOrWhiteSpace(amm))
                                llmSettings.Api.Model = amm.Trim();
                            if (ap.TryGetProperty("key", out var k) && k.GetString() is { } kk && !string.IsNullOrWhiteSpace(kk))
                                llmSettings.Api.Key = kk.Trim();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[WS] settings:llm:set (только provider) от '{wsUser}'");
                    }

                    llmSettings.Provider = provider;
                    llmSettings.Save();
                    llmRouter.InvalidateCapabilities();
                    llmClient.UpdateSettings(llmSettings);
                    eventBus.LlmSettingsSet(true, null);
                    eventBus.LlmSettings(llmSettings);
                    RunSafe.Run(() => PingLlmAndPushAsync(), "PingLlm");
                    break;
                }

                // ── File system ──

                case "fs:list":
                {
                    if (!isAdmin) break;
                    var fsPath = cmd.TryGetProperty("path", out var fpEl) && fpEl.ValueKind == JsonValueKind.String
                        ? fpEl.GetString() : null;
                    var listing = fileBrowser.List(fsPath);
                    eventBus.FsListResult(listing.Path, listing.Parent,
                        listing.Entries.Select(e => new { e.Name, e.Path, e.IsDir, e.Size }).ToList());
                    break;
                }

                case "fs:read":
                {
                    if (!isAdmin) break;
                    var fsPath = cmd.TryGetProperty("path", out var frEl) && frEl.ValueKind == JsonValueKind.String
                        ? frEl.GetString() : null;
                    var content = fileBrowser.Read(fsPath);
                    if (content is null)
                        eventBus.FsReadResult(null, null, false, 0, false);
                    else
                        eventBus.FsReadResult(fsPath, content.Content, content.Truncated, content.Size, content.Binary);
                    break;
                }

                // ── User management ──

                case "user:list":
                {
                    if (!isAdmin) break;
                    var users = userStore.GetAll().Select(u => new
                    {
                        username = u.Username,
                        role = u.Role,
                        createdAtUtc = u.CreatedAtUtc
                    }).ToList();
                    eventBus.UserListResult(users);
                    break;
                }

                case "user:create":
                {
                    if (!isAdmin) break;
                    var uName = cmd.TryGetProperty("username", out var unEl) && unEl.ValueKind == JsonValueKind.String
                        ? unEl.GetString() : null;
                    var uPass = cmd.TryGetProperty("password", out var upEl) && upEl.ValueKind == JsonValueKind.String
                        ? upEl.GetString() : null;
                    var uRole = cmd.TryGetProperty("role", out var urEl) && urEl.ValueKind == JsonValueKind.String
                        ? urEl.GetString() : "user";
                    if (string.IsNullOrEmpty(uName) || string.IsNullOrEmpty(uPass))
                    {
                        eventBus.UserResult(false, "username и password обязательны.");
                        break;
                    }
                    var ok = userStore.CreateUser(uName, uPass, uRole ?? "user", out var err);
                    eventBus.UserResult(ok, err);
                    if (ok)
                    {
                        var users = userStore.GetAll().Select(u => new
                        {
                            username = u.Username,
                            role = u.Role,
                            createdAtUtc = u.CreatedAtUtc
                        }).ToList();
                        eventBus.UserListResult(users);
                    }
                    break;
                }

                case "user:role":
                {
                    if (!isAdmin) break;
                    var uName = cmd.TryGetProperty("username", out var urNameEl) && urNameEl.ValueKind == JsonValueKind.String
                        ? urNameEl.GetString() : null;
                    var uRole = cmd.TryGetProperty("role", out var urRoleEl) && urRoleEl.ValueKind == JsonValueKind.String
                        ? urRoleEl.GetString() : null;
                    if (string.IsNullOrEmpty(uName) || string.IsNullOrEmpty(uRole))
                    {
                        eventBus.UserResult(false, "username и role обязательны.");
                        break;
                    }
                    var ok = userStore.UpdateRole(uName, uRole, out var err);
                    eventBus.UserResult(ok, err);
                    break;
                }

                case "user:delete":
                {
                    if (!isAdmin) break;
                    var uName = cmd.TryGetProperty("username", out var udEl) && udEl.ValueKind == JsonValueKind.String
                        ? udEl.GetString() : null;
                    if (string.IsNullOrEmpty(uName))
                    {
                        eventBus.UserResult(false, "username обязателен.");
                        break;
                    }
                    var ok = userStore.Delete(uName, out var err);
                    eventBus.UserResult(ok, err);
                    break;
                }

                case "user:password":
                {
                    var cur = cmd.TryGetProperty("current", out var pwCurEl) && pwCurEl.ValueKind == JsonValueKind.String
                        ? pwCurEl.GetString() : null;
                    var nxt = cmd.TryGetProperty("next", out var pwNxtEl) && pwNxtEl.ValueKind == JsonValueKind.String
                        ? pwNxtEl.GetString() : null;
                    if (string.IsNullOrEmpty(cur) || string.IsNullOrEmpty(nxt))
                    {
                        eventBus.UserResult(false, "current и next обязательны.");
                        break;
                    }
                    var ok = userStore.ChangePassword(wsUser ?? "", cur, nxt, out var err);
                    eventBus.UserResult(ok, err);
                    break;
                }

                // ── Traffic filter ──

                case "traffic:filter":
                {
                    var filterStr = cmd.TryGetProperty("filter", out var fltEl) && fltEl.ValueKind == JsonValueKind.String
                        ? fltEl.GetString() : "";
                    var all = sniffer.GetRecent(10000);
                    var filtered = TrafficFilterService.Filter(all, filterStr ?? "");
                    eventBus.TrafficFilterResult(filtered.Select(r => new
                    {
                        no = r.Seq,
                        time = r.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                        src = r.SrcIp,
                        dst = r.DstIp,
                        proto = r.Protocol,
                        sport = r.SrcPort,
                        dport = r.DstPort,
                        ttl = r.Ttl,
                        raw = r.Raw is null ? null : Convert.ToHexString(r.Raw),
                        len = r.Length,
                        info = r.Summary ?? "-",
                        seq = r.Seq
                    }).ToList());
                    break;
                }

                // ── Traffic export ──

                case "traffic:export":
                {
                    var format = cmd.TryGetProperty("format", out var fmtEl) && fmtEl.ValueKind == JsonValueKind.String
                        ? fmtEl.GetString() : "csv";
                    var records = sniffer.GetAll();

                    if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                    {
                        var sb = new System.Text.StringBuilder();
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
                        var csv = System.Text.Encoding.UTF8.GetPreamble()
                            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                        eventBus.TrafficExportResult(true, Convert.ToBase64String(csv), "traffic.csv", csv.Length);
                    }
                    else
                    {
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
                        var pcap = ms.ToArray();
                        eventBus.TrafficExportResult(true, Convert.ToBase64String(pcap), "traffic.pcap", pcap.Length);
                    }
                    break;
                }

                // ── Wake word ──

                case "wake:train":
                {
                    var audioB64 = cmd.TryGetProperty("audio", out var wtEl) && wtEl.ValueKind == JsonValueKind.String
                        ? wtEl.GetString() : null;
                    if (string.IsNullOrEmpty(audioB64))
                    {
                        eventBus.WakeTrainResult(false, 0, 0, 0, "audio обязателен.");
                        break;
                    }
                    try
                    {
                        var audioBytes = Convert.FromBase64String(audioB64);
                        var result = wakeWord.Train(audioBytes);
                        eventBus.WakeTrainResult(result.Ok, result.Frames, result.DurationSec,
                            result.TemplatesCount, result.Error);
                        eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                    }
                    catch (Exception ex)
                    {
                        eventBus.WakeTrainResult(false, 0, 0, 0, ex.Message);
                    }
                    break;
                }

                case "wake:reset":
                {
                    wakeWord.ClearTemplates();
                    eventBus.WakeResetResult(true);
                    eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                    break;
                }

                case "wake:check":
                {
                    var audioB64 = cmd.TryGetProperty("audio", out var wcEl) && wcEl.ValueKind == JsonValueKind.String
                        ? wcEl.GetString() : null;
                    if (string.IsNullOrEmpty(audioB64))
                    {
                        eventBus.WakeCheckResult(false, 0, wakeWord.Threshold);
                        break;
                    }
                    try
                    {
                        var audioBytes = Convert.FromBase64String(audioB64);
                        var result = wakeWord.Check(audioBytes);
                        eventBus.WakeCheckResult(result.IsHotword, result.Score, result.Threshold);
                    }
                    catch
                    {
                        eventBus.WakeCheckResult(false, 0, wakeWord.Threshold);
                    }
                    break;
                }

                // ── LLM Profile & Capabilities ──

                case "llm:profile:save":
                {
                    var profileJson = cmd.TryGetProperty("profile", out var pEl) ? pEl.GetRawText() : null;
                    if (!string.IsNullOrEmpty(profileJson))
                    {
                        UserLlmProfile? profile = null;
                        try
                        {
                            profile = System.Text.Json.JsonSerializer.Deserialize<UserLlmProfile>(profileJson, UserLlmProfileJson.Options);
                        }
                        catch (System.Text.Json.JsonException) { }
                        if (profile is not null)
                        {
                            if (userStore.SaveLlmProfile(wsUser ?? "", profile))
                            {
                                llmRouter.InvalidateCapabilities();
                                eventBus.LlmProfileSaved(true);
                            }
                            else
                            {
                                eventBus.LlmProfileSaved(false);
                            }
                        }
                        else
                        {
                            eventBus.LlmProfileSaved(false);
                        }
                    }
                    else
                    {
                        eventBus.LlmProfileSaved(false);
                    }
                    break;
                }

                case "llm:profile:get":
                {
                    var profile = userStore.GetLlmProfile(wsUser ?? "");
                    eventBus.LlmProfile(profile);
                    break;
                }

                case "llm:capabilities:get":
                {
                    var refresh = cmd.TryGetProperty("refresh", out var refEl) && refEl.ValueKind == JsonValueKind.True;
                    var caps = await llmRouter.GetCapabilitiesDtoAsync(refresh);
                    eventBus.LlmCapabilities(caps);
                    break;
                }

                case "llm:ping":
                {
                    var pingProvider = cmd.TryGetProperty("provider", out var pEl) ? pEl.GetString() : null;
                    var pingModel = cmd.TryGetProperty("model", out var mEl) ? mEl.GetString() : null;
                    if (pingProvider is null || pingModel is null) break;
                    var (pingOk, pingMs, pingResolved) = await llmClient.PingAsync(pingProvider, pingModel);
                    eventBus.LlmPingResult(pingProvider, pingModel, pingOk, pingMs, pingResolved);
                    break;
                }

                case "llm:resolve":
                {
                    var resolveTask = cmd.TryGetProperty("task", out var tEl) ? tEl.GetString() : null;
                    var (resolveProvider, resolveModel, resolveOk) = await llmRouter.ResolveBriefAsync(resolveTask ?? "", wsUser ?? "");
                    eventBus.LlmResolveResult(resolveTask, resolveProvider, resolveModel, resolveOk);
                    break;
                }

                case "tts:models":
                {
                    eventBus.TtsModels(tts.ListLocalVoices());
                    break;
                }

                case "tts:get":
                {
                    var confOk = string.Equals(ttsSettings.Engine, "api", StringComparison.OrdinalIgnoreCase)
                        ? !string.IsNullOrWhiteSpace(llmSettings.Api.Key)
                        : tts.IsAvailable;
                    eventBus.TtsConf(ttsSettings.Engine, tts.CurrentVoice(), confOk);
                    break;
                }

                case "tts:set":
                {
                    var ttsEngine = cmd.TryGetProperty("engine", out var engEl) ? engEl.GetString() : null;
                    if (ttsEngine is not ("local" or "api"))
                    {
                        eventBus.TtsSetResult(false, "Неизвестный движок");
                        break;
                    }
                    var ttsVoice = cmd.TryGetProperty("voice", out var vEl) ? vEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(ttsVoice))
                        tts.SetVoice(ttsVoice);
                    var ttsEffective = tts.ResolveVoiceName(string.IsNullOrWhiteSpace(ttsVoice) ? ttsSettings.Voice : ttsVoice) ?? "ru_RU-irina-medium";
                    ttsSettings.Engine = ttsEngine;
                    ttsSettings.Voice = ttsEffective;
                    ttsSettings.Save();
                    eventBus.TtsSetResult(true, null);
                    var effConfOk = string.Equals(ttsSettings.Engine, "api", StringComparison.OrdinalIgnoreCase)
                        ? !string.IsNullOrWhiteSpace(llmSettings.Api.Key)
                        : tts.IsAvailable;
                    eventBus.TtsConf(ttsSettings.Engine, ttsEffective, effConfOk);
                    break;
                }

                case "tts:check":
                {
                    var ttsCheckEngine = cmd.TryGetProperty("engine", out var engEl2) ? engEl2.GetString() : null;
                    var ttsCheckVoice = cmd.TryGetProperty("voice", out var vEl2) ? vEl2.GetString() : null;
                    if (string.Equals(ttsCheckEngine, "api", StringComparison.OrdinalIgnoreCase))
                    {
                        var apiTtsOk = !string.IsNullOrWhiteSpace(llmSettings.Api.Key);
                        var apiTtsVoice = string.IsNullOrWhiteSpace(ttsCheckVoice) ? ttsSettings.Voice : ttsCheckVoice.Trim();
                        eventBus.TtsCheckResult("api", string.IsNullOrWhiteSpace(apiTtsVoice) ? "alloy" : apiTtsVoice, apiTtsOk, 0, apiTtsOk ? "API-ключ задан" : "API-ключ не задан");
                        break;
                    }
                    var localCheckVoice = string.IsNullOrWhiteSpace(ttsCheckVoice) ? tts.CurrentVoice() : ttsCheckVoice.Trim();
                    var (ttsLocalOk, ttsLocalMsg) = tts.CheckLocal(localCheckVoice);
                    eventBus.TtsCheckResult("local", localCheckVoice, ttsLocalOk, 0, ttsLocalMsg);
                    break;
                }

                case "asr:models":
                {
                    eventBus.AsrModels();
                    break;
                }

                case "asr:check":
                {
                    var asrProvider = cmd.TryGetProperty("provider", out var providerEl) ? providerEl.GetString() : null;
                    var asrModel = cmd.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;
                    if (string.Equals(asrProvider, "api", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(llmSettings.Api.Key))
                        {
                            eventBus.AsrCheckResult("api", asrModel, false, 0, "API", "API-ключ не задан");
                            break;
                        }
                        var swAsr = Stopwatch.StartNew();
                        try
                        {
                            var asrApiModels = await llmClient.GetAvailableApiModelsAsync();
                            swAsr.Stop();
                            var apiAsrOk = asrApiModels is { Count: > 0 };
                            eventBus.AsrCheckResult("api", asrModel, apiAsrOk, (long)swAsr.Elapsed.TotalMilliseconds, "API", apiAsrOk ? "Доступен" : "API недоступен (проверьте ключ и ссылку)");
                        }
                        catch (Exception ex)
                        {
                            swAsr.Stop();
                            eventBus.AsrCheckResult("api", asrModel, false, (long)swAsr.Elapsed.TotalMilliseconds, "API", $"API недоступен ({ex.Message})");
                        }
                        break;
                    }
                    var (asrOk, asrEngine, asrMessage) = whisperService.CheckEngine(asrProvider, asrModel);
                    eventBus.AsrCheckResult(asrProvider ?? "local", asrModel, asrOk, 0, asrEngine, asrMessage);
                    break;
                }
            }
        }

        async Task PingLlmAndPushAsync()
        {
            try
            {
                var (ok, ms, model) = await llmClient.PingAsync();
                eventBus.LlmStatus(ok, ms, model);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LLM] Ping error: {ex.Message}");
                eventBus.LlmStatus(false, 0, llmClient.CurrentModel);
            }
        }

        async Task OnWsBinary(byte[] audioData)
        {
            Log.Info($"[VOICE] ← '{RequestContext.Current.Value ?? "unknown"}' аудио {audioData.Length} байт");
            try
            {
                var voiceUser = RequestContext.Current.Value ?? "unknown";
                var speech = userStore.GetLlmProfile(voiceUser).Speech;
                var (text, transcribeError) = await whisperService.TranscribeWithMessageAsync(audioData, speech.Provider, speech.Model, speech.Lang);
                eventBus.VoiceTranscription(text ?? "");

                if (string.IsNullOrWhiteSpace(text))
                {
                    eventBus.VoiceError(transcribeError ?? "Распознавание не удалось");
                    return;
                }

                await processGate.WaitAsync();
                try
                {
                    if (router is not null)
                    {
                        var response = await router.RouteWithResponseAsync(text, username: voiceUser, chatTask: LlmTask.Voice);
                        chatHistory.Add(voiceUser, "user", text);
                        chatHistory.Add(voiceUser, "assistant", response ?? "Готово.");
                        eventBus.VoiceResponse(response ?? "Готово.");
                    }
                }
                finally { processGate.Release(); }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WS:BIN] Voice error: {ex.Message}");
                eventBus.VoiceTranscription("");
                eventBus.VoiceError($"Ошибка распознавания: {ex.Message}");
            }
        }

        async Task OnWsConnect()
        {
            try
            {
                eventBus.IpRegistry(ipRegistry.GetAll());
                eventBus.TrafficLog(sniffer.GetRecent(50));
                eventBus.TrafficCaptureState(sniffer.IsRunning, (ulong)sniffer.TotalCaptured);
                eventBus.NetworkInterfaces(sniffer.ListInterfaces(), sniffer.SelectedIp);
                eventBus.Devices(deviceDiscovery.GetAll());
                eventBus.Tasks(taskRegistry.GetAll());
                eventBus.Patterns(analyzer.GetPatterns());
                eventBus.LlmSettings(llmSettings);
                eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                var historyUser = RequestContext.Current.Value;
                if (!string.IsNullOrWhiteSpace(historyUser))
                {
                    var convList = chatHistory.ListConversations(historyUser);
                    eventBus.ChatList(convList);
                    var lastConvId = chatHistory.GetLastConversationId(historyUser);
                    eventBus.ChatHistory(chatHistory.GetHistory(historyUser, lastConvId));

                    // Send user's LLM profile and capabilities
                    var profile = userStore.GetLlmProfile(historyUser);
                    eventBus.LlmProfile(profile);
                    var caps = await llmRouter.GetCapabilitiesDtoAsync();
                    eventBus.LlmCapabilities(caps);
                }
                RunSafe.Run(() => PingLlmAndPushAsync(), "PingLlm");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WS:CONNECT] Error sending initial state: {ex.Message}");
            }
        }

        // ── Subscribe to sniffer events ──
        var lastBroadcastSeq = 0UL;
        sniffer.OnBatchReady += _ =>
        {
            var (records, seq) = sniffer.GetNewSince(lastBroadcastSeq);
            if (records.Count == 0) return;
            lastBroadcastSeq = seq;
            eventBus.TrafficDelta(records, seq);
        };

        RunSafe.Run(ct => systemStatsService.CollectAndPushAsync(ct), shutdownToken, "SystemStats");

        // Traffic storage cleanup task
        RunSafe.Run(async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    await trafficStorage.CleanupOldRecordsAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Debug.WriteLine($"[STORAGE] Cleanup error: {ex.Message}"); }
            }
        }, shutdownToken, "TrafficStorage.Cleanup");

        var cts = new CancellationTokenSource();
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, cts.Token);

        router = new TaskRouter(registry, llmClient, chatAssistant, llmRouter, ipRegistry, services.GetService<ILogger<TaskRouter>>());

        webServices.OnVoiceProcess = async text =>
        {
            var voiceUser = RequestContext.Current.Value ?? "unknown";
            await processGate.WaitAsync();
            try { return await router.RouteWithResponseAsync(text, username: voiceUser, chatTask: LlmTask.Voice); }
            finally { processGate.Release(); }
        };

        RunSafe.Run(async () =>
        {
            try
            {
                await McpServer.StartAsync(registry, webServices, OnMessage, cts.Token,
                    onCommand: OnWsCommand, onBinary: OnWsBinary, existingSse: sse,
                    onConnect: OnWsConnect,
                    reloadProviders: new object[] { artNetTools, oscTools, networkTools, deviceTools, configTools, adminTools, navigationTools, visionTools });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[START] Web server failed: {ex}");
                cts.Cancel();
            }
        }, "McpServer.Start");

        // Push initial state
        await Task.Delay(2000);
        eventBus.IpRegistry(ipRegistry.GetAll());
        var initialPatterns = analyzer.GetPatterns();
        if (initialPatterns.Count > 0)
            eventBus.Patterns(initialPatterns);

        Console.WriteLine();
        Console.WriteLine("  Web UI:      http://localhost:5000");
        Console.WriteLine("  OSC Server:  port 8000");
        Console.WriteLine("  Вкладка Тест → ArtPoll → симулированные устройства");
        Console.WriteLine();

        await Task.Delay(-1, combinedCts.Token);
    }

    /// <summary>
    /// Legacy entry point — creates services manually. Kept for backward compatibility.
    /// </summary>
    public static async Task RunAsync(string[] args, CancellationToken shutdownToken = default)
    {
        await using var lifetimeManager = new ServiceLifetimeManager();
        var userStore = new UserStore();
        var chatHistory = new ChatHistoryService();
        var llmSettings = LlmSettings.Load();
        var ttsSettings = TtsSettings.Load();
        var llmClient = new LlmClient(llmSettings);
        var llmRouter = new LlmRouter(llmClient, llmClient, userStore, llmSettings);
        var configEditor = new ConfigEditor(llmClient);

        var ipRegistry = new IpRegistryService();
        var sniffer = new TrafficSnifferService(ipRegistry);
        lifetimeManager.Track(sniffer);

        var trafficStorage = new TrafficStorageService();
        lifetimeManager.Track(trafficStorage);
        try { await trafficStorage.InitializeAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] Traffic storage init failed: {ex.Message}"); }
        sniffer.SetStorage(trafficStorage);

        var sse = new SseService();
        var eventBus = new WsEventBus(sse);

        var processGate = new SemaphoreSlim(1, 1);
        TaskRouter? router = null;

        async Task OnMessage(string message)
        {
            await processGate.WaitAsync();
            try
            {
                if (router is not null)
                {
                    var result = await router.RouteWithResponseAsync(message);
                    eventBus.TerminalLine(result ?? "Готово.");
                }
            }
            finally
            {
                processGate.Release();
            }
        }

        TrafficAnalyzerService? analyzer = null;
        analyzer = new TrafficAnalyzerService(llmClient, sniffer, ipRegistry, json =>
        {
            sse.Broadcast(json);
            if (analyzer is not null)
            {
                var pats = analyzer.GetPatterns();
                if (pats.Count > 0)
                    eventBus.Patterns(pats);
            }
        });
        var reportService = new TrafficReportService(trafficStorage, sniffer, ipRegistry, llmClient);
        var artnet = new ArtnetService();
        var osc = new OscService();
        var oscServer = new OscServerService();
        lifetimeManager.Track(oscServer);
        try { oscServer.Start(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] OSC server failed: {ex.Message}"); }
        var artNetTools = new ArtNetTools(artnet, ipRegistry);
        var oscTools = new OscTools(osc);
        var networkTools = new NetworkTools(sniffer, ipRegistry, eventBus);
        var deviceDiscovery = new DeviceDiscoveryService(sniffer, ipRegistry, eventBus);
        var deviceTools = new DeviceTools(new DeviceService(), deviceDiscovery, eventBus);
        var configTools = new ConfigTools(new ConfigService(configEditor.EditAsync));
        var adminTools = new AdminTools();
        var navigationTools = new NavigationTools(eventBus);
        var visionTools = new VisionTools(deviceDiscovery, llmRouter, chatHistory);

        var registry = new ToolRegistry([artNetTools, oscTools, networkTools, deviceTools, configTools, adminTools, navigationTools, visionTools]);

        var devService = new DevService(eventBus);

        var taskRegistry = new TaskRegistryService(sniffer, artnet, oscServer, ipRegistry, eventBus);
        lifetimeManager.Track(taskRegistry);
        try { taskRegistry.Start(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] Task registry failed: {ex.Message}"); }

        var webServices = new WebHostServices
        {
            IpRegistry = ipRegistry,
            Sniffer = sniffer,
            Analyzer = analyzer,
            Artnet = artnet,
            Osc = osc,
            OscServer = oscServer,
            Tasks = taskRegistry,
            FileBrowser = new FileBrowserService(),
            Users = userStore,
            ChatHistory = chatHistory,
            ReportService = reportService,
            LlmClient = llmClient,
        };

        ipRegistry.Register("192.168.1.100", "DMX Controller");
        ipRegistry.Register("192.168.1.50", "OSC Mixer");
        ipRegistry.Register("10.0.0.1", "Lighting Console");
        ipRegistry.Register("10.0.0.2", "LED Panels");
        ipRegistry.Register("172.16.0.10", "Media Server");

        var chatAssistant = new ChatAssistant(llmClient);

        var whisperService = new WhisperService(llmSettings);
        webServices.Whisper = whisperService;

        var wakeWord = new WakeWordService(WakeSettings.Load());
        webServices.WakeWord = wakeWord;
        webServices.OnWakeConfigChanged = () =>
            eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);

        var tts = new TtsService(ttsSettings, llmSettings);
        webServices.Tts = tts;
        RunSafe.Run(() => tts.WarmupAsync(), "TtsService.Warmup");

        var deviceTestService = new DeviceTestService(artnet, osc, oscServer, deviceDiscovery);
        var systemStatsService = new SystemStatsService(taskRegistry, eventBus);
        lifetimeManager.Track(systemStatsService);

        // ── WS command handler ──
        async Task OnWsCommand(JsonElement cmd)
        {
            var type = cmd.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null) return;

            var wsUser = RequestContext.Current.Value;
            var isAdmin = UserStore.Current?.IsAdmin(wsUser) ?? false;

            Log.Info($"[WS] ← '{wsUser ?? "unknown"}' {type}{DescribeWsAction(type, cmd)}");

            static string DescribeWsAction(string actionType, JsonElement cmd)
            {
                string? Str(string name) =>
                    cmd.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                        ? el.GetString()
                        : null;

                string Raw(string name) =>
                    cmd.TryGetProperty(name, out var el) ? el.GetRawText() : "?";

                return actionType switch
                {
                    "chat" or "voice:process" when Str("text") is { } v => $": \"{Log.Truncate(v)}\"",
                    "terminal:exec" when Str("command") is { } c => $": \"{Log.Truncate(c)}\"",
                    "dev:chat" when Str("text") is { } d => $": \"{Log.Truncate(d)}\" agent={Str("agent")} model={Str("model")}",
                    "traffic:interface:set" or "device:test" when Str("ip") is { } ip => $": ip={ip}",
                    "tasks:toggle" or "tasks:run" when Str("id") is { } id => $": id={id}",
                    "settings:llm:set" => $": provider={Str("provider") ?? "?"} model={(cmd.TryGetProperty("api", out var apiEl) && apiEl.TryGetProperty("model", out var am) ? am.GetString() : (cmd.TryGetProperty("local", out var locEl) && locEl.TryGetProperty("model", out var lm) ? lm.GetString() : null))}",
                    "wake:set" => $": enabled={Raw("enabled")}",
                    "wake:threshold" => $": threshold={Raw("threshold")}",
                    _ => ""
                };
            }

            static string? ExtractFirstImage(JsonElement cmd)
            {
                if (!cmd.TryGetProperty("images", out var images)) return null;
                if (images.ValueKind == JsonValueKind.String)
                {
                    var s = images.GetString();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                }
                if (images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in images.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            return item.GetString();
                    }
                }
                return null;
            }

            if (type.StartsWith("dev:", StringComparison.OrdinalIgnoreCase) && !isAdmin)
            {
                Console.WriteLine($"[WS] dev:* denied for '{wsUser}'");
                return;
            }

            switch (type)
            {
                case "artnet:poll":
                {
                    var ips = await artnet.SendPollAsync(sniffer);
                    var devices = ips.Count > 0
                        ? ips.Select(ip => (ip, ipRegistry.Lookup(ip) ?? "Unknown Device")).ToList()
                        : TestDataService.GetSimulatedDevices(ipRegistry);
                    eventBus.ArtPollResult(devices);
                    break;
                }

                case "device:test":
                {
                    var testIp = cmd.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                    var tests = cmd.TryGetProperty("tests", out var testsEl)
                        ? testsEl.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToList()
                        : new List<string>();
                    if (string.IsNullOrWhiteSpace(testIp)) break;
                    var results = await deviceTestService.RunTestsAsync(testIp, tests);
                    eventBus.DeviceTestResult(testIp, results);
                    break;
                }

                case "device:scan":
                {
                    if (deviceDiscovery.IsScanning) break;
                    eventBus.DevicesScanning(true);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await deviceDiscovery.ScanAsync();
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[DEVICE-DISCOVERY] Scan failed: {ex.Message}");
                        }
                        finally
                        {
                            eventBus.DevicesScanning(false);
                        }
                    });
                    break;
                }

                case "camera:open":
                {
                    var cameraTarget = cmd.TryGetProperty("ip", out var camEl) ? camEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(cameraTarget)) break;
                    var cameraIp = deviceDiscovery.ResolveTarget(cameraTarget) ?? cameraTarget;
                    var cameraLabel = deviceDiscovery.CameraLabel(cameraTarget, cameraIp);
                    var cameraUrl = deviceDiscovery.BuildCameraMjpegUrl(cameraIp);
                    eventBus.UIWindowOpen("camera", cameraUrl, cameraLabel);
                    break;
                }

                case "camera:snapshot":
                {
                    var snapTarget = cmd.TryGetProperty("ip", out var snapEl) ? snapEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(snapTarget)) break;

                    var snapIp = deviceDiscovery.ResolveTarget(snapTarget) ?? snapTarget;
                    if (cmd.TryGetProperty("user", out var snapUserEl) && cmd.TryGetProperty("pass", out var snapPassEl))
                    {
                        var snapUser = snapUserEl.GetString();
                        var snapPass = snapPassEl.GetString();
                        if (!string.IsNullOrWhiteSpace(snapUser))
                            deviceDiscovery.SetCameraCredentials(snapIp, snapUser, snapPass);
                    }

                    var snapResult = await visionTools.GetSnapshotAsync(snapTarget);
                    if (snapResult.Bytes is not null && snapResult.Bytes.Length > 0)
                        eventBus.CameraSnapshot(snapTarget, true, Convert.ToBase64String(snapResult.Bytes));
                    else
                        eventBus.CameraSnapshot(snapTarget, false, "", snapResult.Error ?? "Не удалось получить снимок.");
                    break;
                }

                case "camera:analyze":
                {
                    var camTarget = cmd.TryGetProperty("ip", out var camEl2) ? camEl2.GetString() : null;
                    if (string.IsNullOrWhiteSpace(camTarget)) break;
                    var analyzeResult = await visionTools.AnalyzeCamera(camTarget, null, wsUser ?? "");
                    var analyzeOk = !analyzeResult.StartsWith("Ошибка", StringComparison.Ordinal)
                        && !analyzeResult.StartsWith("Не удалось", StringComparison.Ordinal);
                    eventBus.CameraAnalyze(camTarget, analyzeOk, analyzeResult);
                    break;
                }

                case "voice:process":
                {
                    var text = cmd.TryGetProperty("text", out var txtEl) ? txtEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text)) break;

                    var voiceUser = wsUser ?? "unknown";
                    var voiceHistory = chatHistory.GetHistory(voiceUser);
                    await processGate.WaitAsync();
                    string? response = null;
                    try
                    {
                        if (router is not null)
                        {
                            Log.Info($"[AI] → запрос отправлен ИИ (модель: {llmClient.CurrentModel}): \"{Log.Truncate(text)}\"");
                            response = await router.RouteWithResponseAsync(text, voiceHistory, username: voiceUser, chatTask: LlmTask.Voice);
                        }
                        else
                        {
                            Log.Info("[AI] ✗ НЕ отправлено: ядро ещё не готово");
                        }
                    }
                    finally { processGate.Release(); }

                    chatHistory.Add(voiceUser, "user", text);
                    chatHistory.Add(voiceUser, "assistant", response ?? "Готово.");
                    eventBus.VoiceResponse(response ?? "Готово.");
                    break;
                }

                case "voice:speak":
                {
                    var text = cmd.TryGetProperty("text", out var txtEl2) ? txtEl2.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text) || tts is null)
                    {
                        eventBus.VoiceSpeakDone(false, "Синтез речи недоступен");
                        break;
                    }
                    var rate = cmd.TryGetProperty("rate", out var rateEl) && rateEl.TryGetSingle(out var r) ? r : 1f;
                    byte[]? wav;
                    if (string.Equals(ttsSettings.Engine, "api", StringComparison.OrdinalIgnoreCase))
                    {
                        wav = await tts.SynthesizeViaApiAsync(text, ttsSettings.Voice, rate);
                    }
                    else
                    {
                        wav = await tts.SynthesizeAsync(text, rate);
                    }
                    if (wav is not null && wav.Length > 0)
                    {
                        eventBus.VoiceAudio(wav);
                        eventBus.VoiceSpeakDone(true, null);
                    }
                    else
                    {
                        eventBus.VoiceSpeakDone(false, "Синтез речи недоступен");
                    }
                    break;
                }

                case "wake:set":
                {
                    if (cmd.TryGetProperty("enabled", out var enabledEl) && wakeWord is not null)
                    {
                        wakeWord.SetEnabled(enabledEl.GetBoolean());
                        eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                    }
                    break;
                }

                case "wake:threshold":
                {
                    if (cmd.TryGetProperty("threshold", out var thEl) && thEl.TryGetSingle(out var thr) && wakeWord is not null)
                    {
                        wakeWord.SetThreshold(thr);
                        eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                    }
                    break;
                }

                case "terminal:exec":
                {
                    var command = cmd.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(command)) break;
                    await processGate.WaitAsync();
                    try
                    {
                        if (router is not null)
                        {
                            Log.Info($"[AI] → запрос отправлен ИИ (модель: {llmClient.CurrentModel}): \"{Log.Truncate(command)}\"");
                            var result = await router.RouteWithResponseAsync(command);
                            eventBus.TerminalLine(result ?? "Готово.");
                        }
                        else
                        {
                            Log.Info("[AI] ✗ НЕ отправлено: ядро ещё не готово");
                            eventBus.TerminalLine("Ядро ещё не готово, попробуйте чуть позже.");
                        }
                    }
                    finally { processGate.Release(); }
                    break;
                }

                case "traffic:clear":
                {
                    sniffer.Clear();
                    analyzer.ClearPatterns();
                    eventBus.TrafficCleared();
                    break;
                }

                case "traffic:capture_stop":
                {
                    sniffer.Stop();
                    Log.Info($"[WS] ← '{wsUser ?? "unknown"}' захват трафика ОСТАНОВЛЕН");
                    eventBus.TrafficCaptureState(false, (ulong)sniffer.TotalCaptured);
                    break;
                }

                case "traffic:capture_start":
                {
                    sniffer.Start();
                    Log.Info($"[WS] ← '{wsUser ?? "unknown"}' захват трафика ЗАПУЩЕН");
                    eventBus.TrafficCaptureState(true, (ulong)sniffer.TotalCaptured);
                    break;
                }

                case "traffic:interfaces":
                {
                    eventBus.NetworkInterfaces(sniffer.ListInterfaces(), sniffer.SelectedIp);
                    break;
                }

                case "traffic:interface:set":
                {
                    var ifIp = cmd.TryGetProperty("ip", out var ifIpEl) ? ifIpEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(ifIp)) break;

                    var (ok, error) = sniffer.SetInterface(ifIp);
                    if (ok)
                    {
                        sniffer.Clear();
                        eventBus.TrafficCleared();
                    }
                    eventBus.InterfaceSetResult(ok, ifIp, ok ? null : error);
                    eventBus.NetworkInterfaces(sniffer.ListInterfaces(), sniffer.SelectedIp);
                    break;
                }

                case "traffic:patterns_clear":
                {
                    analyzer.ClearPatterns();
                    eventBus.PatternsCleared();
                    break;
                }

                case "chat":
                {
                    var chatText = cmd.TryGetProperty("text", out var chatEl) ? chatEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(chatText)) break;
                    var historyUser = wsUser ?? "unknown";
                    var convId = cmd.TryGetProperty("conversationId", out var cidEl) && cidEl.ValueKind == JsonValueKind.String
                        ? cidEl.GetString() : chatHistory.GetLastConversationId(historyUser);
                    if (convId is null)
                    {
                        var newMeta = chatHistory.CreateConversation(historyUser);
                        convId = newMeta.Id;
                        eventBus.ChatCreated(newMeta);
                    }
                    var historyForLlm = chatHistory.GetHistory(historyUser, convId);
                    var imageDataUrl = ExtractFirstImage(cmd);
                    chatHistory.Add(historyUser, convId, "user", chatText, imageDataUrl);
                    await processGate.WaitAsync();
                    try
                    {
                        if (router is not null)
                        {
                            if (imageDataUrl is not null)
                            {
                                Log.Info($"[AI] → запрос отправлен ИИ (chat + изображение): \"{Log.Truncate(chatText)}\"");
                                var (_, chatOpts, _) = await llmRouter.ResolveAsync(LlmTask.Vision, historyUser);
                                eventBus.ChatThinking("Анализирую запрос…");
                                var sb = new System.Text.StringBuilder();
                                await foreach (var chunk in chatAssistant.AskStreamingAsync(chatText, historyForLlm, chatOpts, imageDataUrl))
                                {
                                    sb.Append(chunk);
                                    eventBus.ChatReasoning(chunk);
                                }
                                var answer = sb.ToString().Trim();
                                if (string.IsNullOrWhiteSpace(answer)) answer = "Не удалось получить ответ.";
                                chatHistory.Add(historyUser, convId, "assistant", answer);
                                eventBus.Chat(answer, done: true);
                            }
                            else
                            {
                                Log.Info($"[AI] → запрос отправлен ИИ (модель: {llmClient.CurrentModel}): \"{Log.Truncate(chatText)}\"");
                                eventBus.ChatThinking("Анализирую запрос…");
                                var answer = await router.RouteWithStreamingAsync(chatText, historyForLlm, async chunk => eventBus.ChatReasoning(chunk), historyUser);
                                chatHistory.Add(historyUser, convId, "assistant", answer ?? "Готово.");
                                eventBus.Chat(answer ?? "Готово.", done: true);
                            }
                        }
                        else
                        {
                            Log.Info("[AI] ✗ НЕ отправлено: ядро ещё не готово");
                            eventBus.Chat("Ядро ещё не готово, попробуйте чуть позже.", done: true);
                        }
                    }
                    finally { processGate.Release(); }
                    break;
                }

                case "chat:clear":
                {
                    var clearConvId = cmd.TryGetProperty("conversationId", out var ccidEl) && ccidEl.ValueKind == JsonValueKind.String
                        ? ccidEl.GetString() : chatHistory.GetLastConversationId(wsUser ?? "");
                    if (clearConvId is not null)
                        chatHistory.Reset(wsUser ?? "", clearConvId);
                    eventBus.ChatHistoryCleared();
                    break;
                }

                case "chat:create":
                {
                    var title = cmd.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                        ? titleEl.GetString() : null;
                    var meta = chatHistory.CreateConversation(wsUser ?? "unknown", title);
                    eventBus.ChatCreated(meta);
                    break;
                }

                case "chat:list":
                {
                    eventBus.ChatList(chatHistory.ListConversations(wsUser ?? "unknown"));
                    break;
                }

                case "chat:switch":
                {
                    var switchConvId = cmd.TryGetProperty("conversationId", out var scidEl) && scidEl.ValueKind == JsonValueKind.String
                        ? scidEl.GetString() : null;
                    if (switchConvId is not null)
                    {
                        var msgs = chatHistory.GetHistory(wsUser ?? "unknown", switchConvId);
                        eventBus.ChatSwitched(switchConvId, msgs);
                    }
                    break;
                }

                case "chat:delete":
                {
                    var delConvId = cmd.TryGetProperty("conversationId", out var dcidEl) && dcidEl.ValueKind == JsonValueKind.String
                        ? dcidEl.GetString() : null;
                    if (delConvId is not null)
                    {
                        chatHistory.DeleteConversation(wsUser ?? "unknown", delConvId);
                        eventBus.ChatDeleted(delConvId);
                    }
                    break;
                }

                case "chat:rename":
                {
                    var renameConvId = cmd.TryGetProperty("conversationId", out var rcidEl) && rcidEl.ValueKind == JsonValueKind.String
                        ? rcidEl.GetString() : null;
                    var newTitle = cmd.TryGetProperty("title", out var rtEl) && rtEl.ValueKind == JsonValueKind.String
                        ? rtEl.GetString() : null;
                    if (renameConvId is not null && newTitle is not null)
                    {
                        chatHistory.RenameConversation(wsUser ?? "unknown", renameConvId, newTitle);
                        eventBus.ChatRenamed(renameConvId, newTitle);
                    }
                    break;
                }

                case "traffic:patterns":
                {
                    var pats = analyzer.GetPatterns();
                    eventBus.Patterns(pats);
                    break;
                }

                case "traffic:test-data":
                {
                    TestDataService.GenerateTestData(sniffer, ipRegistry);
                    eventBus.TrafficLog(sniffer.GetRecent(500));
                    break;
                }

                case "traffic:analysis:instant":
                {
                    var report = await reportService.GenerateInstantReportAsync();
                    eventBus.TrafficReport(report);
                    break;
                }

                case "traffic:analysis:daily":
                {
                    RunSafe.Run(async () =>
                    {
                        var report = await reportService.GenerateDailyReportAsync();
                        eventBus.TrafficReport(report);
                    }, "Report.Daily");
                    break;
                }

                case "traffic:analysis:weekly":
                {
                    RunSafe.Run(async () =>
                    {
                        var report = await reportService.GenerateWeeklyReportAsync();
                        eventBus.TrafficReport(report);
                    }, "Report.Weekly");
                    break;
                }

                case "traffic:explain":
                {
                    var explainType = cmd.TryGetProperty("explain_type", out var etEl) ? etEl.GetString() : "pattern";
                    var targetId = cmd.TryGetProperty("id", out var idEl2) ? idEl2.GetString() : "";
                    var context = cmd.TryGetProperty("context", out var ctxEl) ? ctxEl.GetString() : "";
                    var relatedIpsEl = cmd.TryGetProperty("related_ips", out var ripEl) && ripEl.ValueKind == JsonValueKind.Array
                        ? ripEl.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                        : [];

                    RunSafe.Run(async () =>
                    {
                        var ipsJson = string.Join(", ", relatedIpsEl);
                        var knownIps = ipRegistry.GetAll();
                        var ipNames = string.Join(", ", knownIps.Select(i => $"{i.Ip}={i.Name}"));

                        var typeLabel = explainType switch
                        {
                            "anomaly" => "аномалию в сетевом трафике",
                            "command_pattern" => "паттерн команд в сетевом трафике",
                            _ => "паттерн/событие в сетевом трафике"
                        };

                        var prompt = $"Проанализируй {typeLabel} и дай конкретный ответ по делу.\n\n" +
                            $"ДАННЫЕ:\n{context}\n\n" +
                            $"Связанные IP: {ipsJson}\n" +
                            $"Известные устройства в сети: {ipNames}\n\n" +
                            $"Требования:\n" +
                            $"- Объясни ЧТО именно произошло (конкретно для этих данных, а не вообще)\n" +
                            $"- Какие конкретно риски это создаёт для данной сети\n" +
                            $"- Конкретные рекомендации что делать (не общие слова)\n" +
                            $"- Если есть IP-адреса — скажи какое устройство под ними и что оно делает\n" +
                            $"- Ответь на русском, 4-8 предложений, без воды";

                        var explanation = await llmClient.GenerateAsync(prompt, new LlmRequestOptions(
                            Temperature: 0.3, NumPredict: 512));

                        eventBus.TrafficExplanation(targetId ?? "", explanation ?? "Не удалось получить объяснение.");
                    }, "Report.Explain");
                    break;
                }

                case "tasks:list":
                {
                    eventBus.Tasks(taskRegistry.GetAll());
                    break;
                }

                case "tasks:toggle":
                {
                    var taskId = cmd.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(taskId))
                        taskRegistry.Toggle(taskId);
                    eventBus.Tasks(taskRegistry.GetAll());
                    break;
                }

                case "tasks:run":
                {
                    var taskId = cmd.TryGetProperty("id", out var runEl) ? runEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(taskId))
                        taskRegistry.RunNow(taskId);
                    eventBus.Tasks(taskRegistry.GetAll());
                    break;
                }

                case "dev:auth":
                {
                    var password = cmd.TryGetProperty("password", out var pwEl) ? pwEl.GetString() : null;
                    var ok = !string.IsNullOrEmpty(password) && devService.CheckPassword(password);
                    Console.WriteLine($"[DEV] Auth: {(ok ? "ok" : "failed")}");
                    eventBus.DevAuthResult(ok, devService.CanChangePassword());
                    if (ok)
                        eventBus.DevHistory(devService.GetHistory());
                    break;
                }

                case "dev:options":
                {
                    RunSafe.Run(async () =>
                    {
                        var (agents, models) = await devService.GetOptionsAsync();
                        eventBus.DevOptions(agents, models);
                    }, "Dev.Options");
                    break;
                }

                case "dev:change-password":
                {
                    var current = cmd.TryGetProperty("current", out var curEl) ? curEl.GetString() : null;
                    var next = cmd.TryGetProperty("next", out var nextEl) ? nextEl.GetString() : null;
                    if (devService.TryChangePassword(current ?? "", next ?? "", out var err))
                    {
                        eventBus.DevResponse("Пароль изменён.");
                        eventBus.DevDone();
                    }
                    else
                    {
                        eventBus.DevError(err);
                        eventBus.DevDone();
                    }
                    break;
                }

                case "dev:chat":
                {
                    var text = cmd.TryGetProperty("text", out var txtEl) ? txtEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text)) break;
                    var agent = cmd.TryGetProperty("agent", out var agEl) ? agEl.GetString() : null;
                    var model = cmd.TryGetProperty("model", out var mdEl) ? mdEl.GetString() : null;
                    Console.WriteLine($"[DEV] Chat cmd: agent={agent ?? "(default)"} model={model ?? "(default)"} len={text.Length}");
                    RunSafe.Run(() => devService.SendPromptAsync(text, agent, model), "Dev.Chat");
                    break;
                }

                case "dev:apply":
                {
                    if (cmd.TryGetProperty("files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
                    {
                        var changes = filesEl.Deserialize<DevFileChange[]>(DevService.JsonOptions);
                        Console.WriteLine($"[DEV] Apply cmd: {changes?.Length ?? 0} files");
                        if (changes is not null && changes.Length > 0)
                            RunSafe.Run(() => devService.ApplyChangesAsync(changes), "Dev.Apply");
                    }
                    break;
                }

                case "dev:reject":
                {
                    Console.WriteLine("[DEV] Reject cmd");
                    RunSafe.Run(devService.RejectChangesAsync, "Dev.Reject");
                    break;
                }

                case "dev:new-chat":
                {
                    Console.WriteLine("[DEV] New chat cmd");
                    devService.ResetHistory();
                    eventBus.DevResponse("Новый чат. Контекст очищен.");
                    eventBus.DevDone();
                    break;
                }

                case "settings:llm:get":
                {
                    eventBus.LlmSettings(llmSettings);
                    RunSafe.Run(() => PingLlmAndPushAsync(), "PingLlm");
                    break;
                }

                case "settings:llm:set":
                {
                    var provider = cmd.TryGetProperty("provider", out var p) ? p.GetString() : null;
                    if (provider is not ("local" or "api"))
                    {
                        eventBus.LlmSettingsSet(false, "Неизвестный провайдер");
                        break;
                    }

                    if (isAdmin)
                    {
                        if (cmd.TryGetProperty("local", out var loc))
                        {
                            if (loc.TryGetProperty("url", out var u) && u.GetString() is { } uu && !string.IsNullOrWhiteSpace(uu))
                                llmSettings.Local.Url = uu.Trim();
                            if (loc.TryGetProperty("model", out var m) && m.GetString() is { } mm && !string.IsNullOrWhiteSpace(mm))
                                llmSettings.Local.Model = mm.Trim();
                        }
                        if (cmd.TryGetProperty("api", out var ap))
                        {
                            if (ap.TryGetProperty("baseUrl", out var b) && b.GetString() is { } bb && !string.IsNullOrWhiteSpace(bb))
                                llmSettings.Api.BaseUrl = bb.Trim();
                            if (ap.TryGetProperty("model", out var am) && am.GetString() is { } amm && !string.IsNullOrWhiteSpace(amm))
                                llmSettings.Api.Model = amm.Trim();
                            if (ap.TryGetProperty("key", out var k) && k.GetString() is { } kk && !string.IsNullOrWhiteSpace(kk))
                                llmSettings.Api.Key = kk.Trim();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[WS] settings:llm:set (только provider) от '{wsUser}'");
                    }

                    llmSettings.Provider = provider;
                    llmSettings.Save();
                    llmRouter.InvalidateCapabilities();
                    llmClient.UpdateSettings(llmSettings);
                    eventBus.LlmSettingsSet(true, null);
                    eventBus.LlmSettings(llmSettings);
                    RunSafe.Run(() => PingLlmAndPushAsync(), "PingLlm");
                    break;
                }

                // ── LLM Profile & Capabilities ──

                case "llm:profile:save":
                {
                    var profileJson = cmd.TryGetProperty("profile", out var pEl) ? pEl.GetRawText() : null;
                    if (!string.IsNullOrEmpty(profileJson))
                    {
                        UserLlmProfile? profile = null;
                        try
                        {
                            profile = System.Text.Json.JsonSerializer.Deserialize<UserLlmProfile>(profileJson, UserLlmProfileJson.Options);
                        }
                        catch (System.Text.Json.JsonException) { }
                        if (profile is not null)
                        {
                            if (userStore.SaveLlmProfile(wsUser ?? "", profile))
                            {
                                llmRouter.InvalidateCapabilities();
                                eventBus.LlmProfileSaved(true);
                            }
                            else
                            {
                                eventBus.LlmProfileSaved(false);
                            }
                        }
                        else
                        {
                            eventBus.LlmProfileSaved(false);
                        }
                    }
                    else
                    {
                        eventBus.LlmProfileSaved(false);
                    }
                    break;
                }

                case "llm:profile:get":
                {
                    var profile = userStore.GetLlmProfile(wsUser ?? "");
                    eventBus.LlmProfile(profile);
                    break;
                }

                case "llm:capabilities:get":
                {
                    var refresh = cmd.TryGetProperty("refresh", out var refEl) && refEl.ValueKind == JsonValueKind.True;
                    var caps = await llmRouter.GetCapabilitiesDtoAsync(refresh);
                    eventBus.LlmCapabilities(caps);
                    break;
                }

                case "llm:ping":
                {
                    var pingProvider = cmd.TryGetProperty("provider", out var pEl) ? pEl.GetString() : null;
                    var pingModel = cmd.TryGetProperty("model", out var mEl) ? mEl.GetString() : null;
                    if (pingProvider is null || pingModel is null) break;
                    var (pingOk, pingMs, pingResolved) = await llmClient.PingAsync(pingProvider, pingModel);
                    eventBus.LlmPingResult(pingProvider, pingModel, pingOk, pingMs, pingResolved);
                    break;
                }

                case "llm:resolve":
                {
                    var resolveTask = cmd.TryGetProperty("task", out var tEl) ? tEl.GetString() : null;
                    var (resolveProvider, resolveModel, resolveOk) = await llmRouter.ResolveBriefAsync(resolveTask ?? "", wsUser ?? "");
                    eventBus.LlmResolveResult(resolveTask, resolveProvider, resolveModel, resolveOk);
                    break;
                }

                case "tts:models":
                {
                    eventBus.TtsModels(tts.ListLocalVoices());
                    break;
                }

                case "tts:get":
                {
                    var confOk = string.Equals(ttsSettings.Engine, "api", StringComparison.OrdinalIgnoreCase)
                        ? !string.IsNullOrWhiteSpace(llmSettings.Api.Key)
                        : tts.IsAvailable;
                    eventBus.TtsConf(ttsSettings.Engine, tts.CurrentVoice(), confOk);
                    break;
                }

                case "tts:set":
                {
                    var ttsEngine = cmd.TryGetProperty("engine", out var engEl) ? engEl.GetString() : null;
                    if (ttsEngine is not ("local" or "api"))
                    {
                        eventBus.TtsSetResult(false, "Неизвестный движок");
                        break;
                    }
                    var ttsVoice = cmd.TryGetProperty("voice", out var vEl) ? vEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(ttsVoice))
                        tts.SetVoice(ttsVoice);
                    var ttsEffective = tts.ResolveVoiceName(string.IsNullOrWhiteSpace(ttsVoice) ? ttsSettings.Voice : ttsVoice) ?? "ru_RU-irina-medium";
                    ttsSettings.Engine = ttsEngine;
                    ttsSettings.Voice = ttsEffective;
                    ttsSettings.Save();
                    eventBus.TtsSetResult(true, null);
                    var effConfOk = string.Equals(ttsSettings.Engine, "api", StringComparison.OrdinalIgnoreCase)
                        ? !string.IsNullOrWhiteSpace(llmSettings.Api.Key)
                        : tts.IsAvailable;
                    eventBus.TtsConf(ttsSettings.Engine, ttsEffective, effConfOk);
                    break;
                }

                case "tts:check":
                {
                    var ttsCheckEngine = cmd.TryGetProperty("engine", out var engEl2) ? engEl2.GetString() : null;
                    var ttsCheckVoice = cmd.TryGetProperty("voice", out var vEl2) ? vEl2.GetString() : null;
                    if (string.Equals(ttsCheckEngine, "api", StringComparison.OrdinalIgnoreCase))
                    {
                        var apiTtsOk = !string.IsNullOrWhiteSpace(llmSettings.Api.Key);
                        var apiTtsVoice = string.IsNullOrWhiteSpace(ttsCheckVoice) ? ttsSettings.Voice : ttsCheckVoice.Trim();
                        eventBus.TtsCheckResult("api", string.IsNullOrWhiteSpace(apiTtsVoice) ? "alloy" : apiTtsVoice, apiTtsOk, 0, apiTtsOk ? "API-ключ задан" : "API-ключ не задан");
                        break;
                    }
                    var localCheckVoice = string.IsNullOrWhiteSpace(ttsCheckVoice) ? tts.CurrentVoice() : ttsCheckVoice.Trim();
                    var (ttsLocalOk, ttsLocalMsg) = tts.CheckLocal(localCheckVoice);
                    eventBus.TtsCheckResult("local", localCheckVoice, ttsLocalOk, 0, ttsLocalMsg);
                    break;
                }

                case "asr:models":
                {
                    eventBus.AsrModels();
                    break;
                }

                case "asr:check":
                {
                    var asrProvider = cmd.TryGetProperty("provider", out var providerEl) ? providerEl.GetString() : null;
                    var asrModel = cmd.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;
                    if (string.Equals(asrProvider, "api", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(llmSettings.Api.Key))
                        {
                            eventBus.AsrCheckResult("api", asrModel, false, 0, "API", "API-ключ не задан");
                            break;
                        }
                        var swAsr = Stopwatch.StartNew();
                        try
                        {
                            var asrApiModels = await llmClient.GetAvailableApiModelsAsync();
                            swAsr.Stop();
                            var apiAsrOk = asrApiModels is { Count: > 0 };
                            eventBus.AsrCheckResult("api", asrModel, apiAsrOk, (long)swAsr.Elapsed.TotalMilliseconds, "API", apiAsrOk ? "Доступен" : "API недоступен (проверьте ключ и ссылку)");
                        }
                        catch (Exception ex)
                        {
                            swAsr.Stop();
                            eventBus.AsrCheckResult("api", asrModel, false, (long)swAsr.Elapsed.TotalMilliseconds, "API", $"API недоступен ({ex.Message})");
                        }
                        break;
                    }
                    var (asrOk, asrEngine, asrMessage) = whisperService.CheckEngine(asrProvider, asrModel);
                    eventBus.AsrCheckResult(asrProvider ?? "local", asrModel, asrOk, 0, asrEngine, asrMessage);
                    break;
                }
            }
        }

        // ── Проверка доступности текущего LLM-провайдера (фоновый пинг) ──
        async Task PingLlmAndPushAsync()
        {
            try
            {
                var (ok, ms, model) = await llmClient.PingAsync();
                eventBus.LlmStatus(ok, ms, model);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LLM] Ping error: {ex.Message}");
                eventBus.LlmStatus(false, 0, llmClient.CurrentModel);
            }
        }

        // ── WS binary handler (voice audio): транскрипция → роутер → голосовой ответ ──
        async Task OnWsBinary(byte[] audioData)
        {
            Log.Info($"[VOICE] ← '{RequestContext.Current.Value ?? "unknown"}' аудио {audioData.Length} байт");
            try
            {
                var voiceUser = RequestContext.Current.Value ?? "unknown";
                var speech = userStore.GetLlmProfile(voiceUser).Speech;
                var (text, transcribeError) = await whisperService.TranscribeWithMessageAsync(audioData, speech.Provider, speech.Model, speech.Lang);
                eventBus.VoiceTranscription(text ?? "");

                if (string.IsNullOrWhiteSpace(text))
                {
                    eventBus.VoiceError(transcribeError ?? "Распознавание не удалось");
                    return;
                }

                await processGate.WaitAsync();
                try
                {
                    if (router is not null)
                    {
                        var response = await router.RouteWithResponseAsync(text, username: voiceUser, chatTask: LlmTask.Voice);
                        chatHistory.Add(voiceUser, "user", text);
                        chatHistory.Add(voiceUser, "assistant", response ?? "Готово.");
                        eventBus.VoiceResponse(response ?? "Готово.");
                    }
                }
                finally { processGate.Release(); }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WS:BIN] Voice error: {ex.Message}");
                eventBus.VoiceTranscription("");
                eventBus.VoiceError($"Ошибка распознавания: {ex.Message}");
            }
        }

        // ── WS connect: отдаём новому клиенту текущее состояние ──
        async Task OnWsConnect()
        {
            try
            {
                eventBus.IpRegistry(ipRegistry.GetAll());
                eventBus.TrafficLog(sniffer.GetRecent(50));
                eventBus.TrafficCaptureState(sniffer.IsRunning, (ulong)sniffer.TotalCaptured);
                eventBus.NetworkInterfaces(sniffer.ListInterfaces(), sniffer.SelectedIp);
                eventBus.Devices(deviceDiscovery.GetAll());
                eventBus.Tasks(taskRegistry.GetAll());
                eventBus.Patterns(analyzer.GetPatterns());
                eventBus.LlmSettings(llmSettings);
                eventBus.WakeConfig(wakeWord.Enabled, wakeWord.Threshold, wakeWord.Templates);
                var historyUser = RequestContext.Current.Value;
                if (!string.IsNullOrWhiteSpace(historyUser))
                {
                    var convList = chatHistory.ListConversations(historyUser);
                    eventBus.ChatList(convList);
                    var lastConvId = chatHistory.GetLastConversationId(historyUser);
                    eventBus.ChatHistory(chatHistory.GetHistory(historyUser, lastConvId));

                    var profile = userStore.GetLlmProfile(historyUser);
                    eventBus.LlmProfile(profile);
                    var caps = await llmRouter.GetCapabilitiesDtoAsync();
                    eventBus.LlmCapabilities(caps);
                }
                RunSafe.Run(() => PingLlmAndPushAsync(), "PingLlm");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WS:CONNECT] Error sending initial state: {ex.Message}");
            }
        }

        // ── Подписка на события сервисов ──
        sniffer.OnBatchReady += total =>
        {
            var records = sniffer.GetRecent(15);
            eventBus.TrafficLog(records);
        };

        RunSafe.Run(ct => systemStatsService.CollectAndPushAsync(ct), shutdownToken, "SystemStats");

        // Traffic storage cleanup task
        RunSafe.Run(async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    await trafficStorage.CleanupOldRecordsAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Debug.WriteLine($"[STORAGE] Cleanup error: {ex.Message}"); }
            }
        }, shutdownToken, "TrafficStorage.Cleanup");

        try { sniffer.Start(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] Sniffer failed: {ex.Message}"); }

        var cts = new CancellationTokenSource();
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, cts.Token);

        router = new TaskRouter(registry, llmClient, chatAssistant, llmRouter, ipRegistry);

        webServices.OnVoiceProcess = async text =>
        {
            var voiceUser = RequestContext.Current.Value ?? "unknown";
            await processGate.WaitAsync();
            try { return await router.RouteWithResponseAsync(text, username: voiceUser, chatTask: LlmTask.Voice); }
            finally { processGate.Release(); }
        };

        RunSafe.Run(async () =>
        {
            try
            {
                await McpServer.StartAsync(registry, webServices, OnMessage, cts.Token,
                    onCommand: OnWsCommand, onBinary: OnWsBinary, existingSse: sse,
                    onConnect: OnWsConnect,
                    reloadProviders: new object[] { artNetTools, oscTools, networkTools, deviceTools, configTools, adminTools, navigationTools, visionTools });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[START] Web server failed: {ex}");
                cts.Cancel();
            }
        }, "McpServer.Start");

        // Push initial state
        await Task.Delay(2000);
        eventBus.IpRegistry(ipRegistry.GetAll());
        var initialPatterns = analyzer.GetPatterns();
        if (initialPatterns.Count > 0)
            eventBus.Patterns(initialPatterns);

        Console.WriteLine();
        Console.WriteLine("  Web UI:      http://localhost:5000");
        Console.WriteLine("  OSC Server:  port 8000");
        Console.WriteLine("  Вкладка Тест → ArtPoll → симулированные устройства");
        Console.WriteLine();

        await Task.Delay(-1, combinedCts.Token);
    }

}
