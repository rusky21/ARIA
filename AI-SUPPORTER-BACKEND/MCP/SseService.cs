using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class SseService : IBroadcastService
{
    private const int MaxQueueSize = 256;
    private const int MaxPayloadBytes = 8 * 1024 * 1024;
    private const int MaxBinaryBytes = 16 * 1024 * 1024;

    private sealed class SseClient
    {
        public string? Username;
        public Channel<string> Queue = CreateQueue();
    }

    private sealed class WsClient
    {
        public WebSocket Socket = null!;
        public string? Username;
        public Channel<string> Queue = CreateQueue();
    }

    private static Channel<string> CreateQueue() =>
        Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueueSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private readonly ConcurrentDictionary<Guid, SseClient> _sseClients = new();
    private readonly ConcurrentDictionary<Guid, WsClient> _wsClients = new();
    private static readonly JsonSerializerOptions _json = AppJson.Api;
    private readonly ILogger<SseService>? _logger;

    public SseService(ILogger<SseService>? logger = null)
    {
        _logger = logger;
    }

    public async Task HandleSseAsync(HttpContext context, CancellationToken ct)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var client = new SseClient { Username = CurrentUsername(context) };
        var id = Guid.NewGuid();
        _sseClients[id] = client;

        try
        {
            await context.Response.WriteAsync("data: connected\n\n", ct);
            await context.Response.Body.FlushAsync(ct);

            await foreach (var data in client.Queue.Reader.ReadAllAsync(ct))
            {
                await context.Response.WriteAsync($"data: {data}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            _sseClients.TryRemove(id, out _);
            client.Queue.Writer.TryComplete();
        }
    }

    public async Task HandleWebSocketAsync(
        HttpContext context,
        Func<string, Task>? onMessage,
        CancellationToken ct,
        Func<JsonElement, Task>? onCommand = null,
        Func<byte[], Task>? onBinary = null,
        Func<Task>? onConnect = null)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        var client = new WsClient { Socket = ws, Username = CurrentUsername(context) };
        var id = Guid.NewGuid();
        _wsClients[id] = client;
        RunSafe.Run(ct2 => RunWsSenderAsync(client, ct2), ct, "SseService.WsSender");

        var buffer = new byte[128 * 1024];
        using var textBuffer = new MemoryStream();
        using var binaryBuffer = new MemoryStream();

        try
        {
            if (onConnect is not null)
            {
                try { await onConnect(); }
                catch (Exception ex) { _logger?.LogError(ex, "[WS] onConnect error"); }
            }

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    textBuffer.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                    {
                        if (textBuffer.Length > MaxBinaryBytes)
                        {
                            textBuffer.SetLength(0);
                            _logger?.LogError("[WS] text frame too large, dropping");
                        }
                        continue;
                    }

                    var text = Encoding.UTF8.GetString(textBuffer.ToArray());
                    textBuffer.SetLength(0);

                    if (onCommand is not null)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(text);
                            if (doc.RootElement.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                            {
                                try { _ = onCommand(doc.RootElement); }
                                catch (Exception ex) { _logger?.LogError(ex, "[WS] onCommand error"); }
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "[WS] JSON parse error");
                        }
                    }

                    if (onMessage is not null)
                    {
                        try { _ = onMessage(text); }
                        catch (Exception ex) { _logger?.LogError(ex, "[WS] onMessage error"); }
                    }
                }

                if (result.MessageType == WebSocketMessageType.Binary && onBinary is not null)
                {
                    if (result.EndOfMessage)
                    {
                        binaryBuffer.Write(buffer, 0, result.Count);
                        var data = binaryBuffer.ToArray();
                        binaryBuffer.SetLength(0);
                        try { _ = onBinary(data); }
                        catch (Exception ex) { _logger?.LogError(ex, "[WS] onBinary error"); }
                    }
                    else
                    {
                        binaryBuffer.Write(buffer, 0, result.Count);
                        if (binaryBuffer.Length > MaxBinaryBytes)
                        {
                            binaryBuffer.SetLength(0);
                            _logger?.LogError("[WS] binary message too large, dropping");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _wsClients.TryRemove(id, out _);
            client.Queue.Writer.TryComplete();
        }
    }

    private async Task RunWsSenderAsync(WsClient client, CancellationToken ct)
    {
        try
        {
            await foreach (var message in client.Queue.Reader.ReadAllAsync(ct))
            {
                if (client.Socket.State != WebSocketState.Open) break;
                await client.Socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WS] send loop error");
        }
    }

    public void Broadcast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var payload = message.Replace("\r", " ").Replace("\n", " ");
        SendJsonBytes(Encoding.UTF8.GetBytes(payload));
    }

    public void BroadcastChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var json = JsonSerializer.Serialize(new { type = "chat", text = message }, _json);
        SendJsonBytes(Encoding.UTF8.GetBytes(json));
    }

    public void BroadcastLog(string message, string? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var json = JsonSerializer.Serialize(new { type = "log", text = message, requestId }, _json);
        SendJsonBytes(Encoding.UTF8.GetBytes(json));
    }

    public void BroadcastJson(object data, string? requestId = null)
    {
        var json = JsonSerializer.Serialize(new { type = "json", requestId, payload = data }, _json);
        SendJsonBytes(Encoding.UTF8.GetBytes(json));
    }

    public void SendJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        SendJsonBytes(Encoding.UTF8.GetBytes(json));
    }

    public void SendJsonBytes(byte[] json, string? username = null)
    {
        if (json is null || json.Length == 0) return;
        if (json.Length > MaxPayloadBytes)
        {
            _logger?.LogError("[SSE] payload too large ({ByteCount} bytes), dropping", json.Length);
            return;
        }
        var payload = Encoding.UTF8.GetString(json);

        foreach (var (id, client) in _sseClients)
        {
            if (username is not null && !string.Equals(client.Username, username, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!client.Queue.Writer.TryWrite(payload))
            {
                _sseClients.TryRemove(id, out _);
                client.Queue.Writer.TryComplete();
            }
        }

        foreach (var (id, client) in _wsClients)
        {
            if (username is not null && !string.Equals(client.Username, username, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!client.Queue.Writer.TryWrite(payload))
            {
                _wsClients.TryRemove(id, out _);
                client.Queue.Writer.TryComplete();
            }
        }
    }

    private static string? CurrentUsername(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;
}
