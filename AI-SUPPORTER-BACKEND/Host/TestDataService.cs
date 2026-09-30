namespace AI_Supporter;

/// <summary>
/// Generates simulated traffic data for testing the UI.
/// </summary>
public static class TestDataService
{
    private static readonly HashSet<string> SimulatedIpSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "192.168.1.100", "192.168.1.50", "10.0.0.1", "10.0.0.2", "172.16.0.10"
    };

    public static bool IsSimulatedIp(string ip) => SimulatedIpSet.Contains(ip);

    public static List<(string ip, string name)> GetSimulatedDevices(IpRegistryService ipRegistry) =>
        SimulatedIpSet.Select(ip => (ip, ipRegistry.Lookup(ip) ?? "Unknown Device")).ToList();

    public static void RegisterSimulatedDevices(IpRegistryService ipRegistry)
    {
        ipRegistry.Register("192.168.1.100", "DMX Controller");
        ipRegistry.Register("192.168.1.50", "OSC Mixer");
        ipRegistry.Register("10.0.0.1", "Lighting Console");
        ipRegistry.Register("10.0.0.2", "LED Panels");
        ipRegistry.Register("172.16.0.10", "Media Server");
    }

    public static void GenerateTestData(TrafficSnifferService sniffer, IpRegistryService ipRegistry)
    {
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
            ipRegistry.Register(ip, name);

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
        };
        var artnetPoll = new[]
        {
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
            (s: "10.0.0.1", d: "192.168.1.100", p: "ARTNET", sp: 6454, dp: 6454, len: 530, msg: "Poll (периодический каждые 5с)"),
        };
        var alertFlood = new[]
        {
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
            (s: "10.10.10.10", d: "192.168.1.100", p: "ARTNET", sp: 65333, dp: 6454, len: 530, msg: "DMX ch=1 val=0 (неизвестный источник!)"),
        };
        var normal = new[]
        {
            (s: "172.16.0.10", d: "192.168.1.100", p: "TCP", sp: 443, dp: 34512, len: 1200, msg: "TCP port 443->34512"),
            (s: "192.168.1.100", d: "8.8.8.8", p: "UDP", sp: 56789, dp: 53, len: 80, msg: null),
        };

        var offset = 0;
        foreach (var s in artnetGroup) { sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg)); offset++; }
        offset += 5;
        foreach (var s in oscMacro) { sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg)); offset++; }
        offset += 2;
        foreach (var s in artnetPoll) { sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg)); offset++; }
        offset += 2;
        foreach (var s in alertFlood) { sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg)); offset++; }
        offset += 1;
        foreach (var s in normal) { sniffer.AddRecord(new TrafficRecord(now.AddMilliseconds(-(22000 - offset * 100)), s.s, s.d, s.p, s.sp, s.dp, s.len, s.msg)); offset++; }
    }
}
