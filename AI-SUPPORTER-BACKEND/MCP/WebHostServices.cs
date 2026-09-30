namespace AI_Supporter;

public sealed class WebHostServices
{
    public IpRegistryService? IpRegistry { get; set; }
    public TrafficSnifferService? Sniffer { get; set; }
    public TrafficAnalyzerService? Analyzer { get; set; }
    public ArtnetService? Artnet { get; set; }
    public OscService? Osc { get; set; }
    public OscServerService? OscServer { get; set; }
    public WhisperService? Whisper { get; set; }
    public WakeWordService? WakeWord { get; set; }
    public TtsService? Tts { get; set; }
    public TaskRegistryService? Tasks { get; set; }
    public FileBrowserService? FileBrowser { get; set; }
    public UserStore? Users { get; set; }
    public ChatHistoryService? ChatHistory { get; set; }
    public TrafficReportService? ReportService { get; set; }
    public LlmClient? LlmClient { get; set; }
    public Func<string, Task<string?>>? OnVoiceProcess { get; set; }
    public Action? OnWakeConfigChanged { get; set; }
}
