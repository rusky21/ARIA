using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;

namespace AI_Supporter;

/// <summary>
/// Runs device connectivity tests (ping, http, osc, artnet, camera) against a target IP.
/// </summary>
public sealed class DeviceTestService
{
    private readonly ArtnetService _artnet;
    private readonly OscService _osc;
    private readonly OscServerService _oscServer;
    private readonly DeviceDiscoveryService _discovery;
    private readonly IHttpClientFactory _httpClientFactory;

    public DeviceTestService(ArtnetService artnet, OscService osc, OscServerService oscServer,
        DeviceDiscoveryService? discovery = null, IHttpClientFactory? httpClientFactory = null)
    {
        _artnet = artnet;
        _osc = osc;
        _oscServer = oscServer;
        _discovery = discovery!;
        _httpClientFactory = httpClientFactory ?? new DefaultHttpClientFactory();
    }

    public async Task<List<(string test, bool success, object details)>> RunTestsAsync(
        string testIp, IReadOnlyList<string> tests)
    {
        var results = new List<(string test, bool success, object details)>();

        foreach (var test in tests)
        {
            switch (test)
            {
                case "ping":
                {
                    try
                    {
                        if (TestDataService.IsSimulatedIp(testIp))
                        {
                            results.Add(("ping", true, new { ms = 0, error = (string?)null }));
                        }
                        else
                        {
                            var ping = new Ping();
                            var reply = await ping.SendPingAsync(testIp, 3000);
                            var ok = reply.Status == IPStatus.Success;
                            results.Add(("ping", ok, new { ms = (int)reply.RoundtripTime, error = ok ? null : reply.Status.ToString() }));
                        }
                    }
                    catch (Exception ex) { results.Add(("ping", false, new { ms = 0, error = ex.Message })); }
                    break;
                }
                case "http":
                {
                    try
                    {
                        if (TestDataService.IsSimulatedIp(testIp))
                        {
                            results.Add(("http", true, new { statusCode = 200, error = (string?)null }));
                        }
                        else
                        {
                            using var http = _httpClientFactory.CreateClient();
                            http.Timeout = TimeSpan.FromSeconds(3);
                            var resp = await http.GetAsync($"http://{testIp}");
                            results.Add(("http", true, new { statusCode = (int)resp.StatusCode, error = (string?)null }));
                        }
                    }
                    catch (Exception ex) { results.Add(("http", false, new { statusCode = 0, error = ex.Message })); }
                    break;
                }
                case "osc":
                {
                    try
                    {
                        if (_osc is null) { results.Add(("osc", false, new { command = "/ping", error = "osc not ready" })); }
                        else if (_oscServer is null) { results.Add(("osc", false, new { command = "/ping", error = "osc server not ready" })); }
                        else
                        {
                            var (ok, detail) = await _osc.SendAndVerifyAsync("/ping", testIp, _oscServer);
                            results.Add(("osc", ok, new { command = "/ping", error = ok ? (string?)null : detail, detail = ok ? detail : (string?)null }));
                        }
                    }
                    catch (Exception ex) { results.Add(("osc", false, new { command = "/ping", error = ex.Message })); }
                    break;
                }
                case "artnet":
                {
                    try
                    {
                        if (_artnet is null) { results.Add(("artnet", false, new { universe = 0, channel = 1, value = 255, error = "artnet not ready" })); }
                        else if (TestDataService.IsSimulatedIp(testIp))
                        {
                            results.Add(("artnet", true, new { universe = 0, channel = 1, value = 255, error = (string?)null }));
                        }
                        else
                        {
                            await _artnet.SendBroadcastAsync(0, 1, 255);
                            RunSafe.Run(async () => { await Task.Delay(1000); await _artnet.SendBroadcastAsync(0, 1, 0); }, "Artnet.BroadcastOff");
                            results.Add(("artnet", true, new { universe = 0, channel = 1, value = 255, error = (string?)null }));
                        }
                    }
                    catch (Exception ex) { results.Add(("artnet", false, new { universe = 0, channel = 1, value = 255, error = ex.Message })); }
                    break;
                }
                case "camera":
                {
                    try
                    {
                        if (_discovery is null)
                        {
                            results.Add(("camera", false, new { mjpeg_url = (string?)null, error = "discovery not ready" }));
                        }
                        else
                        {
                            var device = _discovery.GetByIp(testIp);
                            var mjpegUrl = device?.MjpegUrl;
                            if (string.IsNullOrEmpty(mjpegUrl))
                            {
                                results.Add(("camera", false, new { mjpeg_url = (string?)null, error = "no MJPEG stream detected" }));
                            }
                            else
                            {
                                using var http = _httpClientFactory.CreateClient();
                                http.Timeout = TimeSpan.FromSeconds(3);
                                var cleanUrl = DeviceDiscoveryService.StripCameraUserInfo(mjpegUrl);
                                using var req = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
                                var creds = _discovery.CameraCredentialsFor(testIp);
                                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{creds.User}:{creds.Pass}")));
                                var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                                var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                                var ok = resp.IsSuccessStatusCode && (ct.Contains("jpeg") || ct.Contains("mjpeg") || ct.Contains("multipart"));
                                results.Add(("camera", ok, new { mjpeg_url = cleanUrl, content_type = ct, status_code = (int)resp.StatusCode, error = ok ? (string?)null : "unexpected content type" }));
                            }
                        }
                    }
                    catch (Exception ex) { results.Add(("camera", false, new { mjpeg_url = (string?)null, error = ex.Message })); }
                    break;
                }
            }
        }

        return results;
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
