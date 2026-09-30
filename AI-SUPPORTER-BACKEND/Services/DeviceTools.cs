using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class DeviceTools
{
    private readonly DeviceService _device;
    private readonly DeviceDiscoveryService _discovery;
    private readonly WsEventBus _eventBus;
    private readonly ILogger<DeviceTools>? _logger;

    public DeviceTools(DeviceService device, DeviceDiscoveryService discovery, WsEventBus eventBus, ILogger<DeviceTools>? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    [AiTool("refresh_device", "Перезагрузить устройство по IP-адресу или зарегистрированному имени.")]
    public async Task<string> RefreshDevice(
        [AiParameter("IP-адрес устройства или зарегистрированное имя")] string ip)
    {
        var target = _discovery.ResolveTarget(ip) ?? ip;
        var label = target.Equals(ip, StringComparison.OrdinalIgnoreCase) ? ip : $"{ip} ({target})";
        _logger?.LogDebug("[DEVICE] Refresh: {Ip}", target);
        var ok = await _device.RefreshAsync(target);
        return ok
            ? $"Устройство {label} перезагружено."
            : $"Не удалось перезагрузить устройство {label}: не найден endpoint перезагрузки.";
    }

    [AiTool("list_devices", "Получить список найденных устройств в сети.")]
    public string ListDevices()
    {
        var devices = _discovery.GetAll();
        if (devices.Count == 0)
            return "Устройства не найдены. Используй scan_network для сканирования.";

        var lines = new List<string> { $"Найдено устройств: {devices.Count}" };
        foreach (var d in devices)
        {
            var status = d.Online ? "●" : "○";
            var name = string.IsNullOrEmpty(d.Name) ? d.Ip : $"{d.Name} ({d.Ip})";
            lines.Add($"{status} {name} — {d.Type} [{string.Join(", ", d.Services)}]");
        }
        return string.Join("\n", lines);
    }

    [AiTool("scan_network", "Сканировать локальную сеть для поиска устройств (камеры, принтеры, компьютеры, ArtNet, OSC).")]
    public async Task<string> ScanNetwork()
    {
        if (_discovery.IsScanning)
            return "Сканирование уже выполняется...";

        _eventBus.DevicesScanning(true);
        await _discovery.ScanAsync();
        _eventBus.DevicesScanning(false);

        var devices = _discovery.GetAll();
        return $"Сканирование завершено. Найдено устройств: {devices.Count}. Результаты отправлены в интерфейс.";
    }

    [AiTool("open_camera", "Открыть видеопоток камеры во всплывающем окне (MJPEG URL). Принимает IP-адрес или зарегистрированное имя камеры.")]
    public string OpenCamera(
        [AiParameter("IP-адрес камеры или зарегистрированное имя")] string ip)
    {
        var target = _discovery.ResolveTarget(ip) ?? ip;
        var label = _discovery.CameraLabel(ip, target);
        var mjpegUrl = _discovery.BuildCameraMjpegUrl(target);

        _eventBus.UIWindowOpen("camera", mjpegUrl, label);
        return $"Открыт видеопоток камеры {label} ({target}).";
    }

    [AiTool("open_device", "Открыть веб-интерфейс устройства во всплывающем окне (iframe). Принимает IP-адрес или зарегистрированное имя.")]
    public string OpenDevice(
        [AiParameter("IP-адрес устройства или зарегистрированное имя")] string ip)
    {
        var target = _discovery.ResolveTarget(ip) ?? ip;
        var device = _discovery.GetByIp(target);
        var label = target.Equals(ip, StringComparison.OrdinalIgnoreCase)
            ? device?.Name ?? ip
            : ip;

        string url;
        if (device?.Type == DeviceType.Camera)
        {
            url = device.Services.Contains("http") ? $"http://{target}" :
                  device.Services.Contains("https") ? $"https://{target}" :
                  device.Services.Contains("http-alt") ? $"http://{target}:8080" :
                  device.Services.Contains("rtsp") ? $"rtsp://{CameraCredentials.User}:{CameraCredentials.Pass}@{target}/axis-media/media.amp?videocodec=h264" :
                  $"http://{target}";
        }
        else
        {
            url = device is null ? $"http://{target}" :
                  device.Services.Contains("http") ? $"http://{target}" :
                  device.Services.Contains("https") ? $"https://{target}" :
                  device.Services.Contains("http-alt") ? $"http://{target}:8080" :
                  device.Services.Contains("rtsp") ? $"rtsp://{target}:554" :
                  $"http://{target}";
        }

        _eventBus.UIWindowOpen("browser", url, label);
        return $"Открыт интерфейс устройства {label} ({target}).";
    }
}
