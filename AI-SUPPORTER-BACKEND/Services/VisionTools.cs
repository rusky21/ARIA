using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class VisionTools
{
    private readonly DeviceDiscoveryService _discovery;
    private readonly LlmRouter _llmRouter;
    private readonly ChatHistoryService _chatHistory;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<VisionTools>? _logger;

    private static readonly TimeSpan SnapshotCacheTtl = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, (DateTime Time, byte[] Image)> _snapshotCache = new();

    public VisionTools(
        DeviceDiscoveryService discovery,
        LlmRouter llmRouter,
        ChatHistoryService chatHistory,
        IHttpClientFactory? httpClientFactory = null,
        ILogger<VisionTools>? logger = null)
    {
        _discovery = discovery;
        _llmRouter = llmRouter;
        _chatHistory = chatHistory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [AiTool("analyze_camera", "Сделать снимок с камеры и проанализировать через vision LLM. Принимает IP или имя камеры.")]
    public async Task<string> AnalyzeCamera(
        [AiParameter("IP камеры или зарегистрированное имя")] string ip,
        [AiParameter("Запрос/промпт для анализа (опционально)")] string? prompt = null,
        string? username = null)
    {
        var user = username ?? RequestContext.Current.Value ?? "unknown";
        var target = _discovery.ResolveTarget(ip) ?? ip;
        var label = _discovery.CameraLabel(ip, target);

        _logger?.LogInformation("[VISION] Analyze camera: {Target} ({Label}) for user {User}", target, label, user);

        // 1. Get snapshot from camera
        var (imageBytes, _) = await GetSnapshotAsync(target);
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return $"Не удалось получить кадр с камеры {label} ({target}). Проверьте доступность и MJPEG URL.";
        }

        var base64 = Convert.ToBase64String(imageBytes);

        // 2. Resolve model via LlmRouter (Vision task)
        var (client, options, model) = await _llmRouter.ResolveAsync(LlmTask.Vision, user);
        _logger?.LogInformation("[VISION] Using {Provider} model {Model}", client.IsApi ? "API" : "Local", model);

        // 3. Build vision prompt
        var visionPrompt = string.IsNullOrWhiteSpace(prompt)
            ? "Опиши что видишь на кадре с камеры. Детально: люди, объекты, транспорт, текст на табличках, аномалии, освещение."
            : prompt;

        // 4. Call vision LLM
        string? result;
        try
        {
            result = await client.GenerateVisionAsync(visionPrompt, base64, options);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[VISION] Generation failed for {Target}", target);
            return $"Ошибка анализа: {ex.Message}";
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return "Vision модель не вернула результат.";
        }

        // 5. Save to chat history with image reference
        if (!string.IsNullOrWhiteSpace(user))
        {
            var msg = $"[Vision: {label}] {result}";
            _chatHistory.Add(user, "assistant", msg);
        }

        return $"Камера {label} ({target}):\n{result}";
    }

    public async Task<byte[]?> GetSnapshotBytesAsync(string target)
    {
        var resolved = _discovery.ResolveTarget(target) ?? target;
        return (await FetchSnapshotAsync(resolved, null, null)).Bytes;
    }

    public async Task<(byte[]? Bytes, string? Error)> GetSnapshotAsync(string target)
    {
        var resolved = _discovery.ResolveTarget(target) ?? target;
        return await FetchSnapshotAsync(resolved, null, null);
    }

    public async Task<(byte[]? Bytes, string? Error)> GetSnapshotAsync(string target, string? user, string? pass)
    {
        var resolved = _discovery.ResolveTarget(target) ?? target;
        return await FetchSnapshotAsync(resolved, user, pass);
    }

    private async Task<(byte[]? Bytes, string? Error)> FetchSnapshotAsync(string ip, string? user, string? pass)
    {
        var (url, camUser, camPass) = _discovery.CameraSnapshotEndpoint(ip);
        if (string.IsNullOrEmpty(url))
        {
            _logger?.LogWarning("[VISION] No snapshot URL for {Ip}", ip);
            return (null, "Не удалось получить адрес камеры.");
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            camUser = user;
            camPass = pass ?? "";
        }

        // Check cache
        if (_snapshotCache.TryGetValue(ip, out var cached) && DateTime.UtcNow - cached.Time < SnapshotCacheTtl)
        {
            _logger?.LogDebug("[VISION] Cache hit for {Ip}", ip);
            return (cached.Image, null);
        }

        try
        {
            using var http = _httpClientFactory is null ? new HttpClient() : _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "image/jpeg,image/*,multipart/x-mixed-replace");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{camUser}:{camPass}")));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var reason = ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                    ? $"Камера отклонила логин/пароль ({response.StatusCode})."
                    : $"Ошибка HTTP {(int)response.StatusCode}.";
                _logger?.LogWarning("[VISION] HTTP {Status} for {Url}", response.StatusCode, url);
                return (null, reason);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            byte[]? imageBytes = null;

            if (contentType.Contains("multipart"))
            {
                // Parse MJPEG multipart stream - get first frame
                imageBytes = await ExtractFirstMjpegFrame(response.Content);
            }
            else if (contentType.Contains("jpeg") || contentType.Contains("jpg") || contentType.Contains("image"))
            {
                // Direct JPEG
                imageBytes = await response.Content.ReadAsByteArrayAsync();
            }
            else
            {
                // Try to read as bytes anyway
                imageBytes = await response.Content.ReadAsByteArrayAsync();
            }

            if (imageBytes != null && imageBytes.Length > 0)
            {
                if (imageBytes.Length > 4 * 1024 * 1024)
                {
                    _logger?.LogWarning("[VISION] Кадр слишком большой ({Size} байт) для {Ip}, пропускаю", imageBytes.Length, ip);
                    return (null, "Кадр слишком большой.");
                }
                _snapshotCache[ip] = (DateTime.UtcNow, imageBytes);
                return (imageBytes, null);
            }

            return (null, "Камера не вернула кадр (пустой ответ).");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[VISION] Snapshot failed for {Ip}", ip);
            return (null, $"Ошибка запроса: {ex.Message}");
        }
    }

    private static async Task<byte[]?> ExtractFirstMjpegFrame(HttpContent content)
    {
        try
        {
            using var stream = await content.ReadAsStreamAsync();

            int contentLength = -1;
            var headerBytes = new List<byte>(512);
            while (true)
            {
                var next = stream.ReadByte();
                if (next < 0) return null;
                headerBytes.Add((byte)next);
                if (headerBytes.Count >= 4 &&
                    headerBytes[^4] == '\r' && headerBytes[^3] == '\n' &&
                    headerBytes[^2] == '\r' && headerBytes[^1] == '\n')
                {
                    var headerText = System.Text.Encoding.ASCII.GetString(headerBytes.ToArray());
                    foreach (var hdrLine in headerText.Split('\n'))
                    {
                        var line = hdrLine.TrimEnd('\r');
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(line["Content-Length:".Length..].Trim(), out var len))
                        {
                            contentLength = len;
                        }
                    }
                    break;
                }
            }

            if (contentLength <= 0) return null;

            var buffer = new byte[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead));
                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead == contentLength)
                return buffer;
        }
        catch { }
        return null;
    }
}