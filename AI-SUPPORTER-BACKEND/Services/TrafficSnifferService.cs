using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class TrafficSnifferService : IDisposable
{
    private const int MaxRecords = 10000;
    private const int BatchSize = 15;
    private const int RecentWindow = 512;
    private const int MaxRawBytes = 256;
    private readonly ConcurrentQueue<TrafficRecord> _records = new();
    private readonly ConcurrentQueue<TrafficRecord> _recent = new();
    private readonly IpRegistryService _ipRegistry;
    private readonly object _captureLock = new();
    private TrafficStorageService? _storage;
    private CancellationTokenSource? _cts;
    private Socket? _rawSocket;
    private bool _running;
    private int _totalCaptured;
    private int _batchCounter;
    private long _seq;
    private readonly ILogger<TrafficSnifferService>? _logger;

    public event Action<TrafficRecord>? OnPacketCaptured;
    public event Action<int>? OnBatchReady;

    public TrafficSnifferService(IpRegistryService ipRegistry, ILogger<TrafficSnifferService>? logger = null)
    {
        _ipRegistry = ipRegistry;
        _logger = logger;
    }

    public void SetStorage(TrafficStorageService storage) => _storage = storage;

    public bool IsRunning => _running;
    public int TotalCaptured => _totalCaptured;
    public string? SelectedIp { get; private set; }

    public IReadOnlyList<NetworkInterfaceInfo> ListInterfaces()
    {
        var list = new List<NetworkInterfaceInfo>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipv4s = ni.GetIPProperties().UnicastAddresses
                    .Select(u => u.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();
                if (ipv4s.Count == 0) continue;

                var ip = ipv4s.FirstOrDefault(a => !IPAddress.IsLoopback(a) && !IsLinkLocal(a)) ?? ipv4s[0];
                list.Add(new NetworkInterfaceInfo(
                    ni.Id + "|" + ip,
                    ni.Name,
                    ni.Description,
                    ip.ToString(),
                    ni.GetPhysicalAddress().ToString(),
                    ni.NetworkInterfaceType.ToString(),
                    ni.Speed >= 0 ? ni.Speed / 1_000_000 : 0,
                    ni.OperationalStatus == OperationalStatus.Up,
                    IPAddress.IsLoopback(ip)));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SNIFFER] Interface enumeration error");
        }
        return list;
    }

    private static bool IsLinkLocal(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    public void Start()
    {
        CancellationToken token;
        lock (_captureLock)
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        _logger?.LogInformation("[SNIFFER] Starting traffic capture...");

        if (!TryStartRawSocketCapture(SelectedIp, out var error))
        {
            _logger?.LogError("[SNIFFER] Raw socket not available: {Error}", error);
            lock (_captureLock)
            {
                _running = false;
                _cts?.Dispose();
                _cts = null;
            }
            return;
        }

        RunSafe.Run(ct => CaptureLoop(ct), token, "TrafficSniffer.CaptureLoop");
    }

    public void Stop()
    {
        lock (_captureLock)
        {
            if (!_running) return;
            _running = false;
            _cts?.Cancel();
            _rawSocket?.Close();
            _rawSocket = null;
            _cts?.Dispose();
            _cts = null;
        }

        _logger?.LogInformation("[SNIFFER] Stopped");
    }

    public (bool ok, string error) SetInterface(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)
            || !IPAddress.TryParse(ip, out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
            return (false, "Некорректный IP-адрес интерфейса");

        try
        {
            var probe = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            probe.Bind(new IPEndPoint(parsed, 0));
            probe.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            if (OperatingSystem.IsWindows())
                probe.IOControl(IOControlCode.ReceiveAll, [1, 0, 0, 0], null);
            else
                _logger?.LogWarning("[SNIFFER] Non-Windows: raw socket capture without promiscuous mode (limited)");
            probe.Close();
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось привязаться к {ip}: {ex.Message}");
        }

        if (_running) Stop();
        SelectedIp = ip;
        Start();

        return (_rawSocket is not null, _rawSocket is not null ? string.Empty : "Захват не удалось запустить (нужны права администратора)");
    }

    public void Clear()
    {
        _logger?.LogInformation("[SNIFFER] Clear ({TotalCaptured} captured)", _totalCaptured);
        while (_records.TryDequeue(out _)) { }
        while (_recent.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _totalCaptured, 0);
        Interlocked.Exchange(ref _batchCounter, 0);
    }

    public IReadOnlyList<TrafficRecord> GetRecent(int count = 500)
    {
        var snapshot = _recent.ToArray();
        var take = Math.Min(count, snapshot.Length);
        var result = new List<TrafficRecord>(take);
        for (var i = snapshot.Length - 1; i >= snapshot.Length - take; i--)
            result.Add(snapshot[i]);
        return result;
    }

    public (IReadOnlyList<TrafficRecord> Records, ulong Seq) GetNewSince(ulong lastSeq)
    {
        var snapshot = _recent.ToArray();
        var result = new List<TrafficRecord>();
        ulong maxSeq = lastSeq;
        foreach (var r in snapshot)
        {
            if (r.Seq > lastSeq)
            {
                result.Add(r);
                if (r.Seq > maxSeq) maxSeq = r.Seq;
            }
        }
        result.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        return (result, maxSeq);
    }

    public IReadOnlyList<TrafficRecord> GetAll()
    {
        return _records.ToArray();
    }

    public IReadOnlyList<TrafficRecord> GetByType(string type, int count)
    {
        return _records
            .Reverse()
            .Where(r => r.Protocol.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<TrafficRecord> GetByIp(string ip, int count)
    {
        return _records
            .Reverse()
            .Where(r => r.SrcIp.Equals(ip, StringComparison.OrdinalIgnoreCase)
                     || r.DstIp.Equals(ip, StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<TrafficRecord> GetByTypeAndIp(string type, string ip, int count)
    {
        return _records
            .Reverse()
            .Where(r => r.Protocol.Equals(type, StringComparison.OrdinalIgnoreCase)
                     && (r.SrcIp.Equals(ip, StringComparison.OrdinalIgnoreCase)
                      || r.DstIp.Equals(ip, StringComparison.OrdinalIgnoreCase)))
            .Take(count)
            .ToList();
    }

    public TrafficStats GetStats()
    {
        var protocols = _records
            .GroupBy(r => r.Protocol)
            .ToDictionary(g => g.Key, g => g.Count());

        var capturer = _rawSocket is not null ? "RawSocket" : "none";
        return new TrafficStats(_totalCaptured, protocols, capturer);
    }

    public void AddRecord(TrafficRecord record)
    {
        var seq = (ulong)Interlocked.Increment(ref _seq);
        record = record with { Seq = seq };
        _records.Enqueue(record);
        Interlocked.Increment(ref _totalCaptured);

        _recent.Enqueue(record);
        if (_recent.Count > RecentWindow)
            _recent.TryDequeue(out _);

        if (_storage is not null) _storage.Enqueue(record);

        var batch = Interlocked.Increment(ref _batchCounter);
        if (batch >= BatchSize)
        {
            Interlocked.Exchange(ref _batchCounter, 0);
            OnBatchReady?.Invoke(_totalCaptured);
        }

        while (_records.Count > MaxRecords)
        {
            if (!_records.TryDequeue(out _))
                break;
        }
        OnPacketCaptured?.Invoke(record);
    }

    private bool TryStartRawSocketCapture(string? ipOverride, out string error)
    {
        IPAddress? localIp = null;
        if (!string.IsNullOrWhiteSpace(ipOverride))
        {
            if (IPAddress.TryParse(ipOverride, out var parsed)
                && parsed.AddressFamily == AddressFamily.InterNetwork)
                localIp = parsed;
        }
        localIp ??= GetLocalIp();

        if (localIp is null)
        {
            error = "Нет подходящего сетевого интерфейса";
            return false;
        }

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            socket.Bind(new IPEndPoint(localIp, 0));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            if (OperatingSystem.IsWindows())
                socket.IOControl(IOControlCode.ReceiveAll, [1, 0, 0, 0], null);
            _rawSocket = socket;
            SelectedIp = localIp.ToString();
            _logger?.LogInformation("[SNIFFER] Raw socket capture started on {Ip}", localIp);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SNIFFER] Raw socket not available");
            _logger?.LogError("[SNIFFER] Run as Administrator to capture all network traffic");
            error = ex.Message;
            return false;
        }
    }

    private IPAddress? GetLocalIp()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(ip))
                    return ip;
            }
        }
        catch (Exception ex) { _logger?.LogError(ex, "[SNIFFER] DNS resolution failed"); }
        return null;
    }

    private readonly byte[] _captureBuffer = new byte[65535];

    private Task CaptureLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var socket = _rawSocket;
                if (socket is null) break;

                var endPoint = new IPEndPoint(IPAddress.Any, 0) as EndPoint;
                var len = socket.ReceiveFrom(_captureBuffer, ref endPoint);
                if (len > 0)
                    ParseRawPacket(_captureBuffer, len);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SNIFFER] Loop error: {ex.Message}");
            }
        }
        _logger?.LogInformation("[SNIFFER] Capture loop exited");
        return Task.CompletedTask;
    }

    private void ParseRawPacket(byte[] buffer, int length)
    {
        if (length < 20) return;

        var versionAndIhl = buffer[0];
        var ihl = (versionAndIhl & 0x0F) * 4;
        if (ihl < 20 || ihl > length) return;

        var protocolNum = buffer[9];
        var ttl = buffer[8];

        var raw = new byte[Math.Min(length, MaxRawBytes)];
        Buffer.BlockCopy(buffer, 0, raw, 0, raw.Length);

        var srcIp = $"{buffer[12]}.{buffer[13]}.{buffer[14]}.{buffer[15]}";
        var dstIp = $"{buffer[16]}.{buffer[17]}.{buffer[18]}.{buffer[19]}";

        switch (protocolNum)
        {
            case 6 when ihl + 20 <= length:
                ParseTcp(buffer, length, ihl, srcIp, dstIp, raw, ttl);
                break;
            case 17 when ihl + 8 <= length:
                ParseUdpPayload(buffer, length, ihl, srcIp, dstIp, raw, ttl);
                break;
            case 1:
                RecordPacket(srcIp, dstIp, "ICMP", 0, 0, length, null, raw, ttl);
                break;
            default:
                RecordPacket(srcIp, dstIp, $"IP-{protocolNum}", 0, 0, length, null, raw, ttl);
                break;
        }
    }

    private void ParseTcp(byte[] buffer, int length, int ipHeaderLen, string srcIp, string dstIp,
        byte[] raw, byte ttl)
    {
        var offset = ipHeaderLen;
        if (offset + 20 > length) return;

        var srcPort = (buffer[offset] << 8) | buffer[offset + 1];
        var dstPort = (buffer[offset + 2] << 8) | buffer[offset + 3];
        var tcpHeaderLen = (buffer[offset + 12] >> 4) * 4;
        var dataLen = length - offset - tcpHeaderLen;
        var hasPayload = dataLen > 0;

        RecordPacket(srcIp, dstIp, "TCP", srcPort, dstPort, length,
            hasPayload ? $"TCP port {srcPort}->{dstPort}" : null, raw, ttl);
    }

    private void ParseUdpPayload(byte[] buffer, int length, int ipHeaderLen, string srcIp, string dstIp,
        byte[] raw, byte ttl)
    {
        var offset = ipHeaderLen;
        if (offset + 8 > length) return;

        var srcPort = (buffer[offset] << 8) | buffer[offset + 1];
        var dstPort = (buffer[offset + 2] << 8) | buffer[offset + 3];
        var udpLen = (buffer[offset + 4] << 8) | buffer[offset + 5];
        var payloadStart = offset + 8;
        var payloadLen = udpLen >= 8 ? Math.Min(udpLen - 8, length - payloadStart) : 0;

        ParseApplicationProtocol(buffer, payloadStart, payloadLen, srcIp, dstIp, srcPort, dstPort, length, raw, ttl);
    }

    private void ParseApplicationProtocol(byte[] buffer, int start, int len,
        string srcIp, string dstIp, int srcPort, int dstPort, int totalLen,
        byte[] raw, byte ttl)
    {
        if (len <= 0)
        {
            RecordPacket(srcIp, dstIp, "UDP", srcPort, dstPort, totalLen, null, raw, ttl);
            return;
        }

        if ((srcPort == 6454 || dstPort == 6454) && len >= 12)
        {
            var header = Encoding.ASCII.GetString(buffer, start, Math.Min(8, len));
            if (header.StartsWith("Art-Net", StringComparison.Ordinal))
            {
                var opCode = (buffer[start + 9] << 8) | buffer[start + 8];
                var summary = opCode switch
                {
                    0x5000 => $"ArtNet DMX ({buffer[start + 18 + 0]})",
                    0x2000 => "ArtNet Poll",
                    0x2100 => "ArtNet Reply",
                    _ => $"ArtNet OpCode=0x{opCode:X4}"
                };
                RecordPacket(srcIp, dstIp, "ARTNET", srcPort, dstPort, totalLen, summary, raw, ttl);
                return;
            }
        }

        if (len >= 4)
        {
            var firstByte = buffer[start];
            if (firstByte == 0x2F)
            {
                var pathEnd = Array.IndexOf(buffer, 0, start + 1, Math.Min(len - 1, 128));
                if (pathEnd >= start)
                {
                    var pathLen = pathEnd - start;
                    if (pathLen > 0 && pathLen <= 64)
                    {
                        var path = Encoding.ASCII.GetString(buffer, start, pathLen);
                        RecordPacket(srcIp, dstIp, "OSC", srcPort, dstPort, totalLen, path, raw, ttl);
                        return;
                    }
                }
            }
        }

        if (len >= 4 && (dstPort == 80 || srcPort == 80))
        {
            var text = Encoding.ASCII.GetString(buffer, start, Math.Min(len, 256));
            if (text.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("POST ", StringComparison.OrdinalIgnoreCase))
            {
                var firstLine = text.Split('\r', '\n')[0];
                RecordPacket(srcIp, dstIp, "HTTP", srcPort, dstPort, totalLen, firstLine, raw, ttl);
                return;
            }
        }

        RecordPacket(srcIp, dstIp, "UDP", srcPort, dstPort, totalLen, null, raw, ttl);
    }

    private void RecordPacket(string srcIp, string dstIp, string protocol,
        int srcPort, int dstPort, int length, string? summary, byte[]? raw, byte ttl)
    {
        var record = new TrafficRecord(
            DateTime.UtcNow, srcIp, dstIp, protocol,
            srcPort, dstPort, length, summary, raw, ttl);
        AddRecord(record);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
