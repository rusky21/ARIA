using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AI_Supporter;

/// <summary>
/// Centralized DI service registration for the entire application.
/// </summary>
public static class ServiceRegistrar
{
    public static void Configure(IServiceCollection services)
    {
        // ── Logging (Serilog) ──
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // ── Settings (load once, immutable) ──
        var llmSettings = LlmSettings.Load();
        var wakeSettings = WakeSettings.Load();
        var ttsSettings = TtsSettings.Load();

        services.AddSingleton(llmSettings);
        services.AddSingleton(wakeSettings);
        services.AddSingleton(ttsSettings);

        // ── HTTP client factory (pooled connections, prevents socket exhaustion) ──
        services.AddHttpClient();

        // ── Core infrastructure ──
        services.AddSingleton<UserStore>();
        services.AddSingleton<ChatHistoryService>();

        // ── LLM layer ──
        services.AddSingleton(sp => new LlmClient(sp.GetRequiredService<LlmSettings>(), sp.GetService<ILogger<LlmClient>>()));
        services.AddSingleton(sp => new ConfigEditor(sp.GetRequiredService<LlmClient>()));
        services.AddSingleton(sp => new ChatAssistant(sp.GetRequiredService<LlmClient>()));

        // ── Traffic layer ──
        services.AddSingleton<IpRegistryService>();
        services.AddSingleton(sp =>
        {
            var sniffer = new TrafficSnifferService(
                sp.GetRequiredService<IpRegistryService>(),
                sp.GetService<ILogger<TrafficSnifferService>>());
            return sniffer;
        });
        services.AddSingleton<TrafficStorageService>();
        services.AddSingleton(sp =>
        {
            var sse = sp.GetRequiredService<SseService>();
            return new WsEventBus(sse);
        });
        services.AddSingleton(sp =>
        {
            var sniffer = sp.GetRequiredService<TrafficSnifferService>();
            var ipRegistry = sp.GetRequiredService<IpRegistryService>();
            SseService? sse = null;
            WsEventBus? eventBus = null;
            TrafficAnalyzerService? analyzer = null;

            analyzer = new TrafficAnalyzerService(
                sp.GetRequiredService<LlmClient>(),
                sniffer,
                ipRegistry,
                json =>
                {
                    sse?.Broadcast(json);
                    if (analyzer is not null)
                        eventBus?.Patterns(analyzer.GetPatterns());
                });
            return analyzer;
        });
        services.AddSingleton(sp => new TrafficReportService(
            sp.GetRequiredService<TrafficStorageService>(),
            sp.GetRequiredService<TrafficSnifferService>(),
            sp.GetRequiredService<IpRegistryService>(),
            sp.GetRequiredService<LlmClient>()));

        // ── SSE / WS event bus ──
        services.AddSingleton<SseService>();

        // ── Protocol services ──
        services.AddSingleton<ArtnetService>();
        services.AddSingleton<OscService>();
        services.AddSingleton<OscServerService>();

        // ── Tooling ──
        services.AddSingleton<DeviceService>();
        services.AddSingleton(sp =>
        {
            var configEditor = sp.GetRequiredService<ConfigEditor>();
            return new ConfigService(configEditor.EditAsync);
        });

        services.AddSingleton<DevService>();
        services.AddSingleton(sp => new ArtNetTools(
            sp.GetRequiredService<ArtnetService>(),
            sp.GetRequiredService<IpRegistryService>()));
        services.AddSingleton(sp => new OscTools(sp.GetRequiredService<OscService>()));
        services.AddSingleton(sp => new NetworkTools(
            sp.GetRequiredService<TrafficSnifferService>(),
            sp.GetRequiredService<IpRegistryService>(),
            sp.GetRequiredService<WsEventBus>()));
        services.AddSingleton(sp => new DeviceTools(
            sp.GetRequiredService<DeviceService>(),
            sp.GetRequiredService<DeviceDiscoveryService>(),
            sp.GetRequiredService<WsEventBus>(),
            sp.GetService<ILogger<DeviceTools>>()));
        services.AddSingleton(sp => new ConfigTools(sp.GetRequiredService<ConfigService>()));
        services.AddSingleton<AdminTools>();
        services.AddSingleton(sp => new NavigationTools(sp.GetRequiredService<WsEventBus>()));
        services.AddSingleton(sp => new ToolRegistry(new object[]
        {
            sp.GetRequiredService<ArtNetTools>(),
            sp.GetRequiredService<OscTools>(),
            sp.GetRequiredService<NetworkTools>(),
            sp.GetRequiredService<DeviceTools>(),
            sp.GetRequiredService<ConfigTools>(),
            sp.GetRequiredService<AdminTools>(),
            sp.GetRequiredService<NavigationTools>(),
            sp.GetRequiredService<VisionTools>()
        }));

        // ── Task registry ──
        services.AddSingleton<TaskRegistryService>();

        // ── Voice services ──
        services.AddSingleton(sp => new WhisperService(
            sp.GetRequiredService<LlmSettings>(),
            logger: sp.GetService<ILogger<WhisperService>>()));
        services.AddSingleton(sp => new WakeWordService(sp.GetRequiredService<WakeSettings>()));
        services.AddSingleton(sp => new TtsService(
            sp.GetRequiredService<TtsSettings>(),
            sp.GetRequiredService<LlmSettings>(),
            sp.GetService<ILogger<TtsService>>()));

        // ── Composite services ──
        services.AddSingleton<FileBrowserService>();
        services.AddSingleton(sp => new DeviceTestService(
            sp.GetRequiredService<ArtnetService>(),
            sp.GetRequiredService<OscService>(),
            sp.GetRequiredService<OscServerService>(),
            sp.GetRequiredService<DeviceDiscoveryService>(),
            sp.GetService<IHttpClientFactory>()));
        services.AddSingleton(sp => new DeviceDiscoveryService(
            sp.GetRequiredService<TrafficSnifferService>(),
            sp.GetRequiredService<IpRegistryService>(),
            sp.GetRequiredService<WsEventBus>(),
            sp.GetService<ILogger<DeviceDiscoveryService>>(),
            sp.GetService<IHttpClientFactory>()));
        services.AddSingleton(sp => new LlmRouter(
            sp.GetRequiredService<LlmClient>(), // local client
            sp.GetRequiredService<LlmClient>(), // api client (same instance, will switch provider)
            sp.GetRequiredService<UserStore>(),
            sp.GetRequiredService<LlmSettings>(),
            sp.GetService<ILogger<LlmRouter>>()));
        services.AddSingleton(sp => new VisionTools(
            sp.GetRequiredService<DeviceDiscoveryService>(),
            sp.GetRequiredService<LlmRouter>(),
            sp.GetRequiredService<ChatHistoryService>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetService<ILogger<VisionTools>>()));
        services.AddSingleton(sp => new SystemStatsService(
            sp.GetRequiredService<TaskRegistryService>(),
            sp.GetRequiredService<WsEventBus>()));

        // ── App orchestrator ──
        services.AddSingleton<ServiceLifetimeManager>();

        // ── Startup initializer ──
        services.AddSingleton<AppInitializer>();
    }
}
