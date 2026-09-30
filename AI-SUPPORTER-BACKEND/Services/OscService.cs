using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

/// <summary>
/// Сервис отправки OSC (Open Sound Control) команд.
/// </summary>
public sealed class OscService
{
    // Широковещательный адрес по умолчанию
    private const string DefaultIp = "192.168.1.255";

    // Порт по умолчанию
    private const int DefaultPort = 8000;
    private readonly ILogger<OscService>? _logger;

    public OscService(ILogger<OscService>? logger = null)
    {
        _logger = logger;
    }

    public async Task SendAsync(string command)
    {
        await SendAsync(command, DefaultIp);
    }

    public async Task<(bool success, string detail)> SendAndVerifyAsync(string command, string ip, OscServerService server, int timeoutMs = 5000)
    {
        server.Mark();
        await SendAsync(command, ip);
        var ok = await server.WaitForEchoAsync(ip, timeoutMs);
        if (ok)
            return (true, $"echo от {ip}");
        return (false, "нет echo");
    }

    public async Task SendAsync(string command, string ip)
    {
        _logger?.LogInformation("[OSC] Команда: {Command} -> {Ip}:{Port}", command, ip, DefaultPort);

        var parts = command.Trim().Split(' ', 2);
        var path = parts[0];
        var valueStr = parts.Length > 1 ? parts[1].Trim() : "";

        var parseSuccess = int.TryParse(valueStr, out var value);
        if (!parseSuccess && !string.IsNullOrEmpty(valueStr))
        {
            _logger?.LogWarning("[OSC] Предупреждение: не удалось распознать значение '{Value}' (отправка path-only)", valueStr);
        }

        var data = new List<byte>();
        var pathBytes = Encoding.UTF8.GetBytes(path);
        data.AddRange(pathBytes);
        while (data.Count % 4 != 0) data.Add(0);

        if (parseSuccess)
        {
            data.AddRange(",i\0"u8);
            data.Add((byte)((value >> 24) & 0xFF));
            data.Add((byte)((value >> 16) & 0xFF));
            data.Add((byte)((value >> 8) & 0xFF));
            data.Add((byte)(value & 0xFF));
        }
        else
        {
            data.AddRange(",N\0\0"u8);
        }

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        await client.SendAsync(data.ToArray(), data.Count,
            new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ip), DefaultPort));

        _logger?.LogInformation("[OSC] Отправлено -> {Ip}:{Port}", ip, DefaultPort);
    }
}
