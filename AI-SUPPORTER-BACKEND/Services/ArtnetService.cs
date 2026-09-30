using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class ArtnetService
{
    private const int ArtnetPort = 6454;
    private static readonly IPAddress BroadcastAddress = IPAddress.Parse("255.255.255.255");
    private readonly ILogger<ArtnetService>? _logger;

    public ArtnetService(ILogger<ArtnetService>? logger = null)
    {
        _logger = logger;
    }

    public async Task SendAsync(string ip, int universe, int dmx, int value)
    {
        var data = BuildDmxPacket(universe, dmx, value);

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        await client.SendAsync(data, data.Length,
            new IPEndPoint(IPAddress.Parse(ip), ArtnetPort));
        _logger?.LogInformation("[ARTNET] DMX отправлено -> {Ip}:{Port}", ip, ArtnetPort);
    }

    public async Task SendBroadcastAsync(int universe, int dmx, int value)
    {
        await SendAsync("255.255.255.255", universe, dmx, value);
    }

    public async Task<List<string>> SendPollAsync(TrafficSnifferService sniffer)
    {
        var poll = BuildPollPacket();

        var beforeCount = sniffer.TotalCaptured;

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        await client.SendAsync(poll, poll.Length,
            new IPEndPoint(BroadcastAddress, ArtnetPort));
        _logger?.LogInformation("[ARTNET] Poll отправлен на broadcast:{Port}", ArtnetPort);

        await Task.Delay(2000);

        var recent = sniffer.GetRecent(Math.Max(100, sniffer.TotalCaptured - beforeCount + 50));
        var devices = recent
            .Where(r => r.Protocol == "ARTNET"
                && r.Summary != null
                && r.Summary.Contains("ArtNet Reply", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.SrcIp)
            .Distinct()
            .ToList();

        _logger?.LogInformation("[ARTNET] Найдено устройств: {Count}", devices.Count);
        return devices;
    }

    private static byte[] BuildDmxPacket(int universe, int dmx, int value)
    {
        var data = new byte[512 + 18];
        "Art-Net\0"u8.CopyTo(data.AsSpan(0, 8));
        data[8] = 0x00;
        data[9] = 0x50;
        data[10] = 0x00;
        data[11] = 0x0E;
        data[12] = 0x00;
        data[13] = 0x00;
        data[14] = (byte)(universe & 0xFF);
        data[15] = (byte)((universe >> 8) & 0xFF);
        data[16] = 0x02;
        data[17] = 0x00;
        if (dmx >= 1 && dmx <= 512)
            data[18 + dmx - 1] = (byte)value;
        return data;
    }

    private static byte[] BuildPollPacket()
    {
        var data = new byte[14];
        "Art-Net\0"u8.CopyTo(data.AsSpan(0, 8));
        data[8] = 0x00;
        data[9] = 0x20;
        data[10] = 0x00;
        data[11] = 0x0E;
        data[12] = 0x00;
        data[13] = 0x00;
        return data;
    }
}
