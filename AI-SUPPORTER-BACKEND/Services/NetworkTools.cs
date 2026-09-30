using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class NetworkTools
{
    private readonly TrafficSnifferService _sniffer;
    private readonly IpRegistryService _ipRegistry;
    private readonly WsEventBus _eventBus;
    private readonly ILogger<NetworkTools>? _logger;

    public NetworkTools(TrafficSnifferService sniffer, IpRegistryService ipRegistry, WsEventBus eventBus, ILogger<NetworkTools>? logger = null)
    {
        _sniffer = sniffer ?? throw new ArgumentNullException(nameof(sniffer));
        _ipRegistry = ipRegistry ?? throw new ArgumentNullException(nameof(ipRegistry));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    [AiTool("get_traffic_by_type", "Получить последние N пакетов указанного протокола (ArtNet, OSC, TCP, UDP, HTTP, ICMP, IP-{N}).")]
    public Task<string> GetTrafficByType(
        [AiParameter("Тип протокола (ArtNet, Oscar, TCP, UDP, HTTP, ICMP и др.)")] string type,
        [AiParameter("Сколько пакетов вернуть (максимум)", minimum: 1, maximum: 100)] int count)
    {
        _logger?.LogDebug("[NET] GetTrafficByType: proto={Type} count={Count}", type, count);
        var records = _sniffer.GetByType(type, count);
        BroadcastTrafficData(records, "GetTrafficByType", new { type, count });

        var allIps = records.Select(r => r.SrcIp)
            .Concat(records.Select(r => r.DstIp))
            .Distinct().ToList();
        var ipNames = allIps.Select(ip =>
        {
            var name = _ipRegistry.Lookup(ip);
            return name is not null ? $"{ip} ({name})" : ip;
        });

        return Task.FromResult($"Найдено {records.Count} пакетов {type}. Участвующие IP: {string.Join(", ", ipNames)}.");
    }

    [AiTool("get_traffic_by_ip", "Получить последние N пакетов по IP-адресу (отправленные или полученные).")]
    public Task<string> GetTrafficByIp(
        [AiParameter("IP-адрес устройства")] string ip,
        [AiParameter("Сколько пакетов вернуть (максимум)", minimum: 1, maximum: 100)] int count)
    {
        _logger?.LogDebug("[NET] GetTrafficByIp: ip={Ip} count={Count}", ip, count);
        var records = _sniffer.GetByIp(ip, count);
        BroadcastTrafficData(records, "GetTrafficByIp", new { ip, count });

        var name = _ipRegistry.Lookup(ip);
        var ipDisplay = name is not null ? $"{ip} ({name})" : ip;
        var protos = records.Select(r => r.Protocol).Distinct().ToList();

        return Task.FromResult($"Найдено {records.Count} пакетов с/на {ipDisplay}. Протоколы: {string.Join(", ", protos)}.");
    }

    [AiTool("clear_traffic_log", "Очистить лог захваченного трафика.")]
    public Task<string> ClearTrafficLog()
    {
        _logger?.LogDebug("[NET] ClearTrafficLog");
        _sniffer.Clear();
        _eventBus.TrafficCleared();
        return Task.FromResult("Лог трафика очищен.");
    }

    [AiTool("register_ip", "Запомнить имя для IP-адреса.")]
    public Task<string> RegisterIp(
        [AiParameter("IP-адрес устройства")] string ip,
        [AiParameter("Человекочитаемое имя")] string name)
    {
        _logger?.LogDebug("[NET] RegisterIp: {Ip} → {Name}", ip, name);
        _ipRegistry.Register(ip, name);
        _eventBus.IpRegistered(ip, name);
        return Task.FromResult($"IP {ip} зарегистрирован как «{name}».");
    }

    private void BroadcastTrafficData<T>(IReadOnlyList<TrafficRecord> records, string query, T args)
    {
        _eventBus.TrafficQuery(records, query, args);
    }
}
