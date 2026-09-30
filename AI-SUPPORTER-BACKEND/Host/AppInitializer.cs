namespace AI_Supporter;

/// <summary>
/// Performs one-time startup initialization after DI container is built.
/// Called once by Program.cs after the host is built.
/// </summary>
public sealed class AppInitializer
{
    private readonly TrafficStorageService _trafficStorage;
    private readonly TrafficSnifferService _sniffer;
    private readonly IpRegistryService _ipRegistry;
    private readonly OscServerService _oscServer;
    private readonly TaskRegistryService _taskRegistry;
    private readonly TtsService _tts;
    private readonly LlmClient _llm;
    private readonly LlmSettings _llmSettings;

    public AppInitializer(
        TrafficStorageService trafficStorage,
        TrafficSnifferService sniffer,
        IpRegistryService ipRegistry,
        OscServerService oscServer,
        TaskRegistryService taskRegistry,
        TtsService tts,
        LlmClient llm,
        LlmSettings llmSettings)
    {
        _trafficStorage = trafficStorage;
        _sniffer = sniffer;
        _ipRegistry = ipRegistry;
        _oscServer = oscServer;
        _taskRegistry = taskRegistry;
        _tts = tts;
        _llm = llm;
        _llmSettings = llmSettings;
    }

    public async Task InitializeAsync()
    {
        // Initialize traffic storage
        try { await _trafficStorage.InitializeAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] Traffic storage init failed: {ex.Message}"); }

        // Wire storage to sniffer
        _sniffer.SetStorage(_trafficStorage);

        // Register simulated devices
        _ipRegistry.Register("192.168.1.100", "DMX Controller");
        _ipRegistry.Register("192.168.1.50", "OSC Mixer");
        _ipRegistry.Register("10.0.0.1", "Lighting Console");
        _ipRegistry.Register("10.0.0.2", "LED Panels");
        _ipRegistry.Register("172.16.0.10", "Media Server");

        // Start OSC server
        try { _oscServer.Start(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] OSC server failed: {ex.Message}"); }

        // Start task registry
        try { _taskRegistry.Start(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] Task registry failed: {ex.Message}"); }

        // Start traffic sniffer
        try { _sniffer.Start(); }
        catch (Exception ex) { Console.Error.WriteLine($"[START] Sniffer failed: {ex.Message}"); }

        // Warm up TTS
        RunSafe.Run(() => _tts.WarmupAsync(), "TtsService.Warmup");

        // LLM status check
        await Task.Run(() => _llm.DiagnoseLocalAsync());
    }
}
