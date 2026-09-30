using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed record OscResponse(string Path, int? Value, string SenderIp, DateTime Timestamp);

public sealed class OscServerService : IDisposable
{
    private const int Port = 8000;
    private const int MaxQueued = 256;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<OscResponse> _received = new();
    private DateTime _since = DateTime.MinValue;

    public OscResponse? LastReceived { get; private set; }
    public bool SimulationMode { get; set; }

    private readonly object _lock = new();
    private readonly ILogger<OscServerService>? _logger;

    public OscServerService(ILogger<OscServerService>? logger = null)
    {
        _logger = logger;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_listener is not null) return;
            _cts = new CancellationTokenSource();
            _listener = new UdpClient(Port);
            _logger?.LogInformation("[OSC-SRV] Listening on port {Port}", Port);
            _ = ReceiveLoopAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _listener?.Close();
            _listener = null;
        }
    }

    public void Mark()
    {
        _since = DateTime.UtcNow;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync();
                var data = result.Buffer;
                var sender = result.RemoteEndPoint;

                var path = ParseOscPath(data);
                var value = ParseOscInt(data);

                var resp = new OscResponse(path, value, sender.Address.ToString(), DateTime.UtcNow);
                LastReceived = resp;
                _received.Enqueue(resp);
                while (_received.Count > MaxQueued)
                    _received.TryDequeue(out _);

                // Prevent echo loops: don't echo _resp paths back
                if (!path.EndsWith("_resp", StringComparison.Ordinal))
                {
                    var respPath = InsertResp(path);
                    var respData = BuildOscPacket(respPath, value);
                    await _listener.SendAsync(respData, respData.Length, sender);
                    Log.Debug($"[OSC-SRV] Получено от {sender}: {path} = {value} (echo: {respPath})");
                }
                else
                {
                    Log.Debug($"[OSC-SRV] Получено от {sender}: {path} = {value} (echo suppressed)");
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.ConnectionAborted)
            {
                _logger?.LogInformation("[OSC-SRV] Остановлен");
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[OSC-SRV] Ошибка");
            }
        }
    }

    public void InjectEcho(string path, string fromIp)
    {
        var resp = new OscResponse(path, null, fromIp, DateTime.UtcNow);
        LastReceived = resp;
        _received.Enqueue(resp);
        _logger?.LogInformation("[OSC-SRV] Симулирован echo от {FromIp}: {Path}", fromIp, path);
    }

    public async Task<bool> WaitForEchoAsync(string senderIp, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var pollInterval = Math.Min(50, timeoutMs / 10);

        while (DateTime.UtcNow < deadline)
        {
            if (_received.Any(r => r.SenderIp == senderIp && r.Timestamp >= _since))
                return true;
            await Task.Delay(pollInterval);
        }

        if (SimulationMode)
        {
            InjectEcho("(simulated)", senderIp);
            return true;
        }

        return false;
    }

    private static string ParseOscPath(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0x2F) return "?";
        var end = Array.IndexOf(data, (byte)0, 1);
        return end > 0 ? Encoding.UTF8.GetString(data, 0, end) : "?";
    }

    private static int? ParseOscInt(byte[] data)
    {
        var typeTag = Array.IndexOf(data, (byte)',');
        if (typeTag < 0 || typeTag + 4 >= data.Length) return null;
        var typeEnd = typeTag + 1;
        while (typeEnd < data.Length && data[typeEnd] != 0) typeEnd++;
        var aligned = ((typeEnd + 3) / 4) * 4;
        if (aligned + 4 > data.Length) return null;
        return (data[aligned] << 24) | (data[aligned + 1] << 16) | (data[aligned + 2] << 8) | data[aligned + 3];
    }

    private static string InsertResp(string path)
    {
        var slash = path.IndexOf('/', 1);
        var firstSeg = slash < 0 ? path : path[..slash];
        if (firstSeg.EndsWith("_resp"))
            return path;
        if (slash < 0)
            return path + "_resp";
        return path[..slash] + "_resp" + path[slash..];
    }

    private static byte[] BuildOscPacket(string path, int? value)
    {
        var data = new List<byte>();
        var pathBytes = Encoding.UTF8.GetBytes(path);
        data.AddRange(pathBytes);
        while (data.Count % 4 != 0) data.Add(0);

        if (value.HasValue)
        {
            data.AddRange(",i\0"u8);
            data.Add((byte)((value.Value >> 24) & 0xFF));
            data.Add((byte)((value.Value >> 16) & 0xFF));
            data.Add((byte)((value.Value >> 8) & 0xFF));
            data.Add((byte)(value.Value & 0xFF));
        }
        else
        {
            data.AddRange(",N\0\0"u8);
        }

        return data.ToArray();
    }

    public void Dispose() => Stop();
}
