using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

/// <summary>
/// Сервис управления устройством: перезагрузка VAPIX-камер (Axis),
/// а также базовый HTTP-ребут для устройств с открытым management API.
/// </summary>
public sealed class DeviceService
{
    private readonly ILogger<DeviceService>? _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public DeviceService(ILogger<DeviceService>? logger = null, IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> RefreshAsync(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out _))
        {
            _logger?.LogWarning("[DEVICE] Invalid IP: {Ip}", ip);
            return false;
        }

        _logger?.LogInformation("[DEVICE] Refresh (reboot) {Ip}", ip);

        var rebootAttempts = new[]
        {
            $"http://{ip}/axis-cgi/system.operation?action=reboot",   // Axis VAPIX
            $"http://{ip}/cgi-bin/rest.cgi",                          // некоторые ONVIF-камеры
        };

        foreach (var url in rebootAttempts)
        {
            try
            {
                var http = CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{CameraCredentials.User}:{CameraCredentials.Pass}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                using var response = await http.SendAsync(request);
                _logger?.LogInformation("[DEVICE] Reboot attempt {Url} -> {Status}", url, (int)response.StatusCode);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger?.LogInformation("[DEVICE] Reboot accepted for {Ip}", ip);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[DEVICE] Reboot attempt {Url} failed", url);
            }
        }

        _logger?.LogWarning("[DEVICE] Reboot failed for {Ip} (no VAPIX endpoint found)", ip);
        return false;
    }

    private HttpClient CreateClient()
    {
        if (_httpClientFactory is not null)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);
            return client;
        }
        return new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    }
}