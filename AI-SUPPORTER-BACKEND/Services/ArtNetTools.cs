using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class ArtNetTools
{
    private readonly ArtnetService _artnet;
    private readonly IpRegistryService _ipRegistry;
    private readonly ILogger<ArtNetTools>? _logger;

    public ArtNetTools(ArtnetService artnet, IpRegistryService ipRegistry, ILogger<ArtNetTools>? logger = null)
    {
        _artnet = artnet ?? throw new ArgumentNullException(nameof(artnet));
        _ipRegistry = ipRegistry ?? throw new ArgumentNullException(nameof(ipRegistry));
        _logger = logger;
    }

    [AiTool("send_artnet", "Отправить значение DMX по ArtNet. Используй для управления светом. Принимает IP-адрес или зарегистрированное имя устройства.")]
    public async Task<string> SendArtnet(
        [AiParameter("IP-адрес устройства или зарегистрированное имя (например, 192.168.1.1)")] string ip,
        [AiParameter("Номер вселенной ArtNet (0-15)", minimum: 0, maximum: 15)] int universe,
        [AiParameter("Адрес канала DMX (1-512)", minimum: 1, maximum: 512)] int dmx,
        [AiParameter("Значение канала (0-255)", minimum: 0, maximum: 255)] int value)
    {
        var target = _ipRegistry.ResolveToIp(ip) ?? ip;
        _logger?.LogDebug("[ARTNET] Send: {Ip} u={Universe} ch={Dmx} = {Value}", target, universe, dmx, value);
        await _artnet.SendAsync(target, universe, dmx, value);
        var label = target.Equals(ip, StringComparison.OrdinalIgnoreCase) ? ip : $"{ip} ({target})";
        return $"Отправлен Art-Net: {label} юниверс {universe} канал {dmx} = {value}";
    }
}
