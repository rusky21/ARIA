using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class DeviceDiscoveryService
{
    private const string FileName = "devices.json";
    private readonly TrafficSnifferService _sniffer;
    private readonly IpRegistryService _ipRegistry;
    private readonly WsEventBus _eventBus;
    private readonly ILogger<DeviceDiscoveryService>? _logger;
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, (string User, string Pass)> _deviceCredentials = new(StringComparer.OrdinalIgnoreCase);
    private List<DiscoveredDevice> _devices = [];
    private bool _scanning;

    private static readonly Dictionary<DeviceType, string[]> TypeKeywords = new()
    {
        [DeviceType.Camera] = ["camera", "ipcam", "webcam", "hikvision", "dahua", "reolink", "axis", "foscam", "mjpeg", "video"],
        [DeviceType.Printer] = ["printer", "print", "hp", "canon", "epson", "brother", "lexmark"],
        [DeviceType.Switch] = ["switch", "router", "gateway", "ubiquiti", "mikrotik", "cisco", "netgear", "tplink", "tp-link", "asus", "dlink"],
        [DeviceType.Computer] = ["computer", "pc", "laptop", "windows", "linux", "mac", "macbook", "iphone", "ipad", "android", "samsung", "xiaomi", "huawei", "oneplus", "pixel", "realme", "oppo", "vivo", "sony", "lg", "nokia", "motorola"],
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public DeviceDiscoveryService(
        TrafficSnifferService sniffer,
        IpRegistryService ipRegistry,
        WsEventBus eventBus,
        ILogger<DeviceDiscoveryService>? logger = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _sniffer = sniffer;
        _ipRegistry = ipRegistry;
        _eventBus = eventBus;
        _logger = logger;
        _httpClientFactory = httpClientFactory ?? new DefaultHttpClientFactory();
        _filePath = ResolvePath(FileName);
        Load();
    }

    public IReadOnlyList<DiscoveredDevice> GetAll()
    {
        lock (_lock) return _devices.ToList();
    }

    public DiscoveredDevice? GetByIp(string ip)
    {
        lock (_lock) return _devices.FirstOrDefault(d =>
            string.Equals(d.Ip, ip, StringComparison.OrdinalIgnoreCase));
    }

    public DiscoveredDevice? GetByName(string name)
    {
        lock (_lock) return _devices.FirstOrDefault(d =>
            string.Equals(d.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string? ResolveTarget(string nameOrIp)
    {
        var byName = GetByName(nameOrIp);
        if (byName is not null)
            return byName.Ip;

        return _ipRegistry.ResolveToIp(nameOrIp);
    }

    public string CameraLabel(string nameOrIp, string target)
    {
        return target.Equals(nameOrIp, StringComparison.OrdinalIgnoreCase)
            ? GetByIp(target)?.Name ?? nameOrIp
            : nameOrIp;
    }

    public string BuildCameraMjpegUrl(string ip)
    {
        var device = GetByIp(ip);
        var url = device?.MjpegUrl;
        if (string.IsNullOrEmpty(url))
            url = $"http://{ip}/axis-cgi/mjpg/video.cgi";
        return InjectCreds(url, ip);
    }

    public (string Url, string User, string Pass) CameraSnapshotEndpoint(string ip)
    {
        var device = GetByIp(ip);
        var url = device?.MjpegUrl;
        if (string.IsNullOrEmpty(url))
            url = $"http://{ip}/axis-cgi/mjpg/video.cgi";
        var clean = StripCameraUserInfo(url);
        var axisIdx = clean.IndexOf("/axis-cgi/mjpg/video.cgi", StringComparison.Ordinal);
        if (axisIdx >= 0)
            clean = $"{clean[..axisIdx]}/axis-cgi/jpg/image.cgi";
        var creds = CameraCredentialsFor(ip);
        return (clean, creds.User, creds.Pass);
    }

    public static string StripCameraUserInfo(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        var at = url.IndexOf('@');
        if (scheme >= 0 && at > scheme)
            return $"{url[..(scheme + 3)]}{url[(at + 1)..]}";
        return url;
    }

    public (string User, string Pass) CameraCredentialsFor(string ip)
    {
        if (_deviceCredentials.TryGetValue(ip.Trim(), out var creds))
            return creds;
        return (CameraCredentials.User, CameraCredentials.Pass);
    }

    public void SetCameraCredentials(string ip, string? user, string? pass)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        _deviceCredentials[ip.Trim()] = (user.Trim(), pass ?? "");
    }

    public static string InjectCreds(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Contains('@')) return url;
        var idx = url.IndexOf("://", StringComparison.Ordinal);
        if (idx < 0) return url;
        return $"{url[..(idx + 3)]}{CameraCredentials.User}:{CameraCredentials.Pass}@{url[(idx + 3)..]}";
    }

    private string InjectCreds(string? url, string ip)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Contains('@')) return url;
        var idx = url.IndexOf("://", StringComparison.Ordinal);
        if (idx < 0) return url;
        var creds = CameraCredentialsFor(ip);
        return $"{url[..(idx + 3)]}{creds.User}:{creds.Pass}@{url[(idx + 3)..]}";
    }

    public void UpsertDevice(DiscoveredDevice device)
    {
        lock (_lock)
        {
            var existing = _devices.FirstOrDefault(d =>
                string.Equals(d.Ip, device.Ip, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                _devices.Remove(existing);
            _devices.Add(device);
            Save();
        }
    }

    public void RemoveDevice(string ip)
    {
        lock (_lock)
        {
            _devices.RemoveAll(d => string.Equals(d.Ip, ip, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }

    public bool IsScanning => _scanning;

    public async Task ScanAsync(CancellationToken ct = default)
    {
        if (_scanning) return;
        _scanning = true;

        try
        {
            var baseIps = GetBaseIps();
            _logger?.LogInformation("[DEVICE-DISCOVERY] Starting scan, subnets: {Subnets}",
                string.Join(", ", baseIps));

            var arpTask = Task.Run(LoadArpTable, ct);
            var upnpTask = ProbeUpnpSsdpAsync(ct);
            var onvifTask = ProbeWsDiscoveryAllAsync(ct);

            var allHosts = new HashSet<string>();

            foreach (var baseIp in baseIps)
            {
                var parts = baseIp.Split('.');
                if (parts.Length != 4) continue;
                var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}.";
                for (var i = 1; i <= 254; i++)
                    allHosts.Add($"{prefix}{i}");
            }

            var onlineHosts = new List<string>();
            var semaphore = new SemaphoreSlim(64);

            var tasks = allHosts.Select(async ip =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(ip, 750);
                    if (reply.Status == IPStatus.Success)
                    {
                        lock (onlineHosts) onlineHosts.Add(ip);
                    }
                }
                catch (Exception ex) { _logger?.LogDebug(ex, "[DEVICE-DISCOVERY] Ping {Ip} failed", ip); }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);

            var arpTable = await arpTask;
            var upnpDevices = await upnpTask;
            var onvifHosts = await onvifTask;

            _logger?.LogInformation("[DEVICE-DISCOVERY] Found {Count} online hosts: {Hosts}",
                onlineHosts.Count, string.Join(", ", onlineHosts.Take(20)) + (onlineHosts.Count > 20 ? "..." : ""));

            _logger?.LogInformation("[DEVICE-DISCOVERY] WS-Discovery found {Count} ONVIF devices: {Ips}",
                onvifHosts.Count, string.Join(", ", onvifHosts));

            var probeHosts = new HashSet<string>(onlineHosts, StringComparer.OrdinalIgnoreCase);
            foreach (var ip in upnpDevices.Keys) probeHosts.Add(ip);
            foreach (var ip in onvifHosts) probeHosts.Add(ip);

            _logger?.LogInformation("[DEVICE-DISCOVERY] Probe candidates (ICMP + UPnP + ONVIF): {Count}",
                probeHosts.Count);

            var discovered = new List<DiscoveredDevice>();

            var probeTasks = probeHosts.Select(async ip =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var device = await ProbeHostAsync(ip, onvifHosts, upnpDevices, arpTable, ct);
                    if (device is not null)
                    {
                        lock (discovered) discovered.Add(device);
                        UpsertLive(device);
                        _eventBus.DeviceFound(device);
                    }
                }
                catch (Exception ex) { _logger?.LogWarning(ex, "[DEVICE-DISCOVERY] Probe {Ip} failed", ip); }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(probeTasks);

            lock (_lock)
            {
                var foundIps = new HashSet<string>(
                    discovered.Select(d => d.Ip), StringComparer.OrdinalIgnoreCase);

                foreach (var known in _devices)
                {
                    if (!foundIps.Contains(known.Ip))
                        known.Online = false;
                }

                Save();
            }

            _eventBus.Devices(_devices);
            _logger?.LogInformation("[DEVICE-DISCOVERY] Scan complete: {Count} devices", _devices.Count);
        }
        finally
        {
            _scanning = false;
        }
    }

    private void UpsertLive(DiscoveredDevice device)
    {
        lock (_lock)
        {
            var existing = _devices.FirstOrDefault(x =>
                string.Equals(x.Ip, device.Ip, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                _devices.Remove(existing);
            _devices.Add(device);
            Save();
        }
    }

    private async Task<DiscoveredDevice?> ProbeHostAsync(string ip, ISet<string> onvifHosts,
        Dictionary<string, UpnpDeviceInfo> upnpDevices, Dictionary<string, string> arpTable, CancellationToken ct)
    {
        var device = new DiscoveredDevice
        {
            Ip = ip,
            LastSeen = DateTime.UtcNow
        };

        if (arpTable.TryGetValue(ip, out var mac))
        {
            device.Mac = mac;
            var vendor = DetectVendorByMac(mac);
            if (vendor is not null)
                device.Vendor = vendor;
        }

        if (upnpDevices.TryGetValue(ip, out var upnp))
        {
            device.Vendor = upnp.Manufacturer;
            device.Name = !string.IsNullOrEmpty(upnp.ModelName) ? upnp.ModelName : upnp.FriendlyName;
            device.Notes = $"UPnP: {upnp.Manufacturer} {upnp.ModelName}";
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var services = new List<string>();

        var portDefs = new[]
        {
            (Port: 80, Name: "http"), (Port: 443, Name: "https"), (Port: 8080, Name: "http-alt"),
            (Port: 8081, Name: "http-alt2"), (Port: 554, Name: "rtsp"), (Port: 8000, Name: "rtsp-alt"),
            (Port: 9000, Name: "osc"), (Port: 6454, Name: "artnet")
        };

        using (var portsCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            portsCts.CancelAfter(2000);
            var portResults = await Task.WhenAll(portDefs.Select(async p =>
                new { p.Name, Ok = await ProbeTcpAsync(ip, p.Port, portsCts.Token) }));
            foreach (var pr in portResults)
                if (pr.Ok)
                    services.Add(pr.Name);
        }

        sw.Stop();
        device.ResponseMs = (int)sw.ElapsedMilliseconds;
        device.Services = services;
        device.Online = true;

        _logger?.LogDebug("[DEVICE-DISCOVERY] Probe {Ip}: ports={Ports}, elapsed={Ms}ms",
            ip, services.Count > 0 ? string.Join(",", services) : "none", sw.ElapsedMilliseconds);

        if (services.Count == 0 && device.Vendor is null)
            return null;

        var isAxis = device.Vendor?.Contains("Axis", StringComparison.OrdinalIgnoreCase) == true;

        if (services.Contains("http") || services.Contains("https") || services.Contains("http-alt") || services.Contains("http-alt2"))
        {
            device.MjpegUrl = await DetectMjpegAsync(ip, ct);
            var serverHeader = await GetHttpServerHeaderAsync(ip, ct);
            device.Type = DetectTypeByHttp(serverHeader);

            if (device.Type == DeviceType.Unknown && isAxis)
                device.Type = DeviceType.Camera;

            _logger?.LogDebug("[DEVICE-DISCOVERY] {Ip} HTTP header: '{Header}', type: {Type}, mjpeg: {Mjpeg}",
                ip, serverHeader ?? "(empty)", device.Type, device.MjpegUrl ?? "(none)");
        }

        if (device.Type == DeviceType.Unknown && isAxis)
            device.Type = DeviceType.Camera;

        if (services.Contains("artnet"))
            device.Type = DeviceType.ArtNet;

        if (services.Contains("osc") && device.Type == DeviceType.Unknown)
            device.Type = DeviceType.Osc;

        if (device.Type == DeviceType.Unknown && services.Contains("rtsp"))
            device.Type = DeviceType.Camera;

        if (device.Type == DeviceType.Unknown && services.Contains("rtsp-alt"))
            device.Type = DeviceType.Camera;

        if (device.Type == DeviceType.Unknown && onvifHosts.Contains(ip))
            device.Type = DeviceType.Camera;

        if (device.Type == DeviceType.Camera && (string.IsNullOrEmpty(device.Name) || device.Name == ip))
        {
            var vapix = await QueryVapixBasicDeviceInfoAsync(ip, ct);
            if (vapix?.Model is not null)
            {
                device.Name = vapix.Model;
                device.Vendor ??= "Axis";
                var notesParts = new List<string>();
                if (vapix.SerialNumber is not null) notesParts.Add($"SN:{vapix.SerialNumber}");
                if (vapix.Firmware is not null) notesParts.Add($"FW:{vapix.Firmware}");
                if (notesParts.Count > 0)
                    device.Notes = string.Join(", ", notesParts);
            }
        }

        var registryName = _ipRegistry.Lookup(ip);
        if (!string.IsNullOrEmpty(registryName) && string.IsNullOrEmpty(device.Name))
            device.Name = registryName;

        if (device.Type == DeviceType.Camera && !string.IsNullOrEmpty(device.Name) && device.Name != ip)
            _ipRegistry.Register(ip, device.Name);

        _logger?.LogDebug("[DEVICE-DISCOVERY] Probe {Ip}: final type={Type}, vendor={Vendor}, name={Name}",
            ip, device.Type, device.Vendor ?? "(none)", device.Name ?? "(none)");
        return device;
    }

    private static async Task<bool> ProbeTcpAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);
            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch { return false; }
    }

    private static async Task<HttpResponseMessage?> SendWithBasicAuth(HttpClient http, string url, string user, string pass, CancellationToken ct)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
            return await http.SendAsync(req, ct);
        }
        catch { return null; }
    }

    private async Task<HashSet<string>> ProbeWsDiscoveryAllAsync(CancellationToken ct)
    {
        var onvifHosts = new HashSet<string>();

        var soapProbe =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\"" +
            " xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\"" +
            " xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\"" +
            " xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">" +
            "<e:Header>" +
            "<w:MessageID>uuid:" + Guid.NewGuid() + "</w:MessageID>" +
            "<w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>" +
            "<w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>" +
            "</e:Header>" +
            "<e:Body>" +
            "<d:Probe>" +
            "<d:Types>dn:NetworkVideoTransmitter</d:Types>" +
            "</d:Probe>" +
            "</e:Body>" +
            "</e:Envelope>";

        try
        {
            using var udp = new UdpClient();
            var multicastEp = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702);
            var data = Encoding.UTF8.GetBytes(soapProbe);

            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
            await udp.SendAsync(data, data.Length, multicastEp);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);

            while (udp.Available > 0 || await WaitForDataAsync(udp, cts.Token))
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var response = Encoding.UTF8.GetString(result.Buffer);

                if (response.Contains("NetworkVideoTransmitter") ||
                    response.Contains("NetworkVideo"))
                {
                    if (result.RemoteEndPoint is not null)
                        onvifHosts.Add(result.RemoteEndPoint.Address.ToString());
                }
            }
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "[DEVICE-DISCOVERY] WS-Discovery failed"); }

        return onvifHosts;
    }

    private static async Task<bool> WaitForDataAsync(UdpClient udp, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (udp.Available > 0) return true;
                await Task.Delay(100, ct);
            }
        }
        catch { }
        return false;
    }

    private async Task<string?> DetectMjpegAsync(string ip, CancellationToken ct)
    {
        var endpoints = new[]
        {
            "/axis-cgi/mjpg/video.cgi",
            "/axis-cgi/jpg/image.cgi",
            "/jpg/image.jpg",
            "/axis-cgi/jpg/image.cgi?resolution=1920x1080",
            "/videostream.cgi",
            "/snapshot.cgi",
            "/video",
            "/cgi-bin/snapshot.cgi",
            "/cgi-bin/video.cgi",
            "/mjpg/video.mjpg",
            "/ISAPI/Streaming/channels/101/httpPreview",
        };

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(2);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AXIS");

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(1200);
            var creds = CameraCredentialsFor(ip);

            var results = await Task.WhenAll(endpoints.Select(async ep =>
            {
                try
                {
                    var uri = $"http://{ip}{ep}";
                    var resp = await http.GetAsync(uri, cts.Token);
                    var media = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (resp.IsSuccessStatusCode && (media.Contains("jpeg") || media.Contains("mjpeg") || media.Contains("multipart")))
                        return uri;

                    if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        var authResp = await SendWithBasicAuth(http, uri, creds.User, creds.Pass, cts.Token);
                        if (authResp is not null)
                        {
                            var authMedia = authResp.Content.Headers.ContentType?.MediaType ?? "";
                            if (authResp.IsSuccessStatusCode && (authMedia.Contains("jpeg") || authMedia.Contains("mjpeg") || authMedia.Contains("multipart")))
                                return uri;
                        }
                    }
                }
                catch { }

                return null;
            }));

            return results.FirstOrDefault(u => u is not null);
        }
    }

    private async Task<string?> GetHttpServerHeaderAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(2);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AXIS");

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(1500);
                var creds = CameraCredentialsFor(ip);

                var authResp = await SendWithBasicAuth(http, $"http://{ip}", creds.User, creds.Pass, cts.Token);
                if (authResp is not null)
                {
                    var header = authResp.Headers.Server.ToString();
                    if (!string.IsNullOrEmpty(header))
                        return header;
                }

                var getResp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://{ip}"), cts.Token);
                return getResp.Headers.Server.ToString();
            }
        }
        catch { return null; }
    }

    private static DeviceType DetectTypeByHttp(string? serverHeader)
    {
        if (string.IsNullOrEmpty(serverHeader)) return DeviceType.Unknown;
        var lower = serverHeader.ToLowerInvariant();

        if (lower.Contains("hikvision") || lower.Contains("dahua") || lower.Contains("reolink") ||
            lower.Contains("axis") || lower.Contains("ipcam") || lower.Contains("goscam") ||
            lower.Contains("webcam") || lower.Contains("netcam") || lower.Contains("motion"))
            return DeviceType.Camera;

        if (lower.Contains("printer") || lower.Contains("hpgl") || lower.Contains("xerox"))
            return DeviceType.Printer;

        if (lower.Contains("cisco") || lower.Contains("ubiquiti") || lower.Contains("mikrotik") ||
            lower.Contains("dd-wrt") || lower.Contains("openwrt") || lower.Contains("nginx") && lower.Contains("router"))
            return DeviceType.Switch;

        return DeviceType.Unknown;
    }

    private List<string> GetBaseIps()
    {
        var ips = new HashSet<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ua.Address)) continue;
                    var ip = ua.Address.ToString();
                    var parts = ip.Split('.');
                    if (parts.Length == 4)
                        ips.Add($"{parts[0]}.{parts[1]}.{parts[2]}");
                }
            }
        }
        catch { }

        if (ips.Count == 0)
            ips.Add("192.168.1");

        return ips.Select(p => $"{p}.0").ToList();
    }

    private async Task<Dictionary<string, UpnpDeviceInfo>> ProbeUpnpSsdpAsync(CancellationToken ct)
    {
        var devices = new Dictionary<string, UpnpDeviceInfo>();
        const string msearch =
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 3\r\n" +
            "ST: ssdp:all\r\n" +
            "\r\n";

        try
        {
            using var udp = new UdpClient();
            var endpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            var data = Encoding.UTF8.GetBytes(msearch);

            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
            await udp.SendAsync(data, data.Length, endpoint);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);

            while (udp.Available > 0 || await WaitForDataAsync(udp, cts.Token))
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var response = Encoding.UTF8.GetString(result.Buffer);
                var ip = result.RemoteEndPoint?.Address.ToString();
                if (string.IsNullOrEmpty(ip)) continue;

                var location = ExtractHeader(response, "LOCATION");
                if (string.IsNullOrEmpty(location)) continue;

                var deviceInfo = await FetchUpnpDescriptionAsync(location, cts.Token);
                if (deviceInfo is not null)
                    devices[ip] = deviceInfo;
            }
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "[DEVICE-DISCOVERY] UPnP SSDP failed"); }

        _logger?.LogInformation("[DEVICE-DISCOVERY] UPnP found {Count} devices: {Ips}",
            devices.Count, string.Join(", ", devices.Keys));
        return devices;
    }

    private async Task<UpnpDeviceInfo?> FetchUpnpDescriptionAsync(string locationUrl, CancellationToken ct)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(2);
            var xml = await http.GetStringAsync(locationUrl, ct);

            var manufacturer = ExtractXmlTag(xml, "manufacturer") ?? "";
            var modelName = ExtractXmlTag(xml, "modelName") ?? "";
            var friendlyName = ExtractXmlTag(xml, "friendlyName") ?? "";

            if (string.IsNullOrEmpty(manufacturer) && string.IsNullOrEmpty(modelName))
                return null;

            return new UpnpDeviceInfo
            {
                Manufacturer = manufacturer,
                ModelName = modelName,
                FriendlyName = friendlyName
            };
        }
        catch { return null; }
    }

    private static string? ExtractHeader(string httpHeaders, string headerName)
    {
        foreach (var line in httpHeaders.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(headerName, StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':');
                return idx >= 0 ? line[(idx + 1)..].Trim() : null;
            }
        }
        return null;
    }

    private static string? ExtractXmlTag(string xml, string tagName)
    {
        var pattern = $"<{tagName}[^>]*>([^<]*)</{tagName}>";
        var match = Regex.Match(xml, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static Dictionary<string, string> LoadArpTable()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("arp", "-a")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var proc = Process.Start(psi);
            if (proc is null) return result;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                var ipCandidate = parts[0];
                var macCandidate = parts[1];

                if (IPAddress.TryParse(ipCandidate, out _) &&
                    Regex.IsMatch(macCandidate, @"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$"))
                {
                    var normalizedMac = macCandidate.Replace('-', ':').ToUpperInvariant();
                    if (normalizedMac.StartsWith("FF:FF:FF") || normalizedMac.StartsWith("01:00:5E"))
                        continue;
                    result[ipCandidate] = normalizedMac;
                }
            }
        }
        catch { }
        return result;
    }

    private static string? DetectVendorByMac(string? mac)
    {
        if (string.IsNullOrEmpty(mac)) return null;
        var prefix = mac.Replace(":", "").Replace("-", "").ToUpperInvariant()[..6];
        return prefix switch
        {
            "00408C" => "Axis",
            "001CB1" => "Axis",
            "ACCC8E" => "Axis",
            _ => null
        };
    }

    private async Task<VapixDeviceInfo?> QueryVapixBasicDeviceInfoAsync(string ip, CancellationToken ct)
    {
        var urls = new[]
        {
            $"http://{ip}/axis-cgi/basicdeviceinfo.cgi",
            $"http://{ip}/axis-cgi/basicdeviceinfo.cgi?media=bandwidth"
        };

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(3000);
            var creds = CameraCredentialsFor(ip);

            foreach (var url in urls)
            {
                try
                {
                    using var http = _httpClientFactory.CreateClient();
                    http.Timeout = TimeSpan.FromSeconds(2);
                    var resp = await SendWithBasicAuth(http, url, creds.User, creds.Pass, cts.Token);
                    if (resp is not null && resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(cts.Token);
                        var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;

                        var model = root.TryGetProperty("propertyList", out var plist) &&
                                    plist.TryGetProperty("model", out var modelEl)
                            ? modelEl.GetString() : null;

                        var serial = root.TryGetProperty("propertyList", out var plist2) &&
                                     plist2.TryGetProperty("serialNumber", out var serialEl)
                            ? serialEl.GetString() : null;

                        var firmware = root.TryGetProperty("propertyList", out var plist3) &&
                                       plist3.TryGetProperty("firmwareVersion", out var fwEl)
                            ? fwEl.GetString() : null;

                        if (!string.IsNullOrEmpty(model))
                            return new VapixDeviceInfo { Model = model, SerialNumber = serial, Firmware = firmware };
                    }
                }
                catch (Exception ex) { _logger?.LogDebug(ex, "[DEVICE-DISCOVERY] VAPIX query {Url} failed", url); }
            }

            try
            {
                using var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(2);
                var resp = await SendWithBasicAuth(http, $"http://{ip}/axis-cgi/param.cgi?action=list&group=Brand", creds.User, creds.Pass, cts.Token);
                if (resp is not null && resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(cts.Token);
                    var model = ExtractParamValue(body, "Brand.Model");
                    var serial = ExtractParamValue(body, "System.SerialNumber");
                    var firmware = ExtractParamValue(body, "System.Firmware.Version");
                    if (!string.IsNullOrEmpty(model))
                        return new VapixDeviceInfo { Model = model, SerialNumber = serial, Firmware = firmware };
                }
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "[DEVICE-DISCOVERY] VAPIX param.cgi failed for {Ip}", ip); }
        }

        return null;
    }

    private static string? ExtractParamValue(string response, string paramName)
    {
        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(paramName + "=", StringComparison.OrdinalIgnoreCase))
                return trimmed[(paramName.Length + 1)..].Trim().Trim('"');
        }
        return null;
    }

    private sealed class UpnpDeviceInfo
    {
        public string Manufacturer { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
    }

    private sealed class VapixDeviceInfo
    {
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? Firmware { get; set; }
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) { _devices = []; Save(); return; }
            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<DeviceSnapshot>(json, JsonOpts);
            _devices = snapshot?.Devices ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEVICE-DISCOVERY] Load error");
            _devices = [];
        }
    }

    private void Save()
    {
        try
        {
            var snapshot = new DeviceSnapshot { Devices = _devices, ScanTime = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEVICE-DISCOVERY] Save error");
        }
    }

    private static string ResolvePath(string fileName)
    {
        var dirs = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
        };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path)) return path;
        }
        return Path.Combine(Environment.CurrentDirectory, fileName);
    }
}
