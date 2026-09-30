using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

/// <summary>
/// Клиент LLM с двумя транспортами: local (Ollama /api/generate) и api (OpenAI-совместимый chat/completions).
/// Провайдер переключается на лету через UpdateSettings.
/// </summary>
public sealed class LlmClient
{
    private volatile LlmSettings _settings;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    // Глобальный шлюз: один запрос LLM за раз + пауза между запросами (для api и local).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _nextAllowedTicks;
    private long _cooldownUntilTicks;
    private readonly ILogger<LlmClient>? _logger;
    private readonly HashSet<string> _blockedApiModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _blockedLock = new();

    public LlmClient(LlmSettings settings, ILogger<LlmClient>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    public bool IsApi => _settings.Provider == "api";
    public string CurrentModel => IsApi ? _settings.Api.Model : _settings.EffectiveLocalModel;

    public void BlockApiModel(string model) =>
        Lock(_blockedLock, () => _blockedApiModels.Add(model));

    public bool IsApiModelBlocked(string model) =>
        Lock(_blockedLock, () => _blockedApiModels.Contains(model));

    public void ClearBlockedApiModels() =>
        Lock(_blockedLock, () => _blockedApiModels.Clear());

    private static void Lock(object sync, Action action)
    {
        lock (sync) action();
    }

    private static T Lock<T>(object sync, Func<T> action)
    {
        lock (sync) return action();
    }

    private bool ShouldUseApi(LlmRequestOptions? options) => options?.UseApi ?? _settings.Provider == "api";

    public void UpdateSettings(LlmSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ClearBlockedApiModels();
        _logger?.LogInformation("[LLM] Провайдер: {Provider}", _settings.Provider == "api" ? "API (" + _settings.Api.BaseUrl + ", " + _settings.Api.Model + ")" : "Local (" + _settings.EffectiveLocalUrl + ", " + _settings.EffectiveLocalModel + ")");
    }

    public async Task<string?> GenerateAsync(string prompt, LlmRequestOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Log.Info("[LLM] ✗ НЕ отправлено: пустой prompt");
            return null;
        }

        _logger?.LogInformation("[LLM] Request: provider={Provider} model={Model} len={Len} temp={Temp}", ShouldUseApi(options) ? "api" : "local", options?.Model ?? CurrentModel, prompt.Length, options?.Temperature ?? 0.1);

        var sw = Stopwatch.StartNew();
        string? result;
        await _gate.WaitAsync();
        try
        {
            await WaitForPacingAsync(ShouldUseApi(options));
            result = ShouldUseApi(options)
                ? await GenerateApiAsync(prompt, options)
                : await GenerateLocalAsync(prompt, options);
        }
        finally
        {
            _gate.Release();
        }

        sw.Stop();
        if (result is not null)
            Log.Info($"[LLM] ✓ Ответ ИИ получен ({result.Length} симв., {sw.ElapsedMilliseconds} мс)");
        else
            Log.Info("[LLM] ✗ ИИ не ответил (причина в ошибке выше)");

        return result;
    }

    public async Task<string?> GenerateAsync(IReadOnlyList<object> messages, LlmRequestOptions? options = null)
    {
        if (messages.Count == 0)
        {
            Log.Info("[LLM] ✗ НЕ отправлено: пустой messages");
            return null;
        }

        _logger?.LogInformation("[LLM] Request (messages): provider={Provider} model={Model} count={Count}", ShouldUseApi(options) ? "api" : "local", options?.Model ?? CurrentModel, messages.Count);

        var sw = Stopwatch.StartNew();
        string? result;
        await _gate.WaitAsync();
        try
        {
            await WaitForPacingAsync(ShouldUseApi(options));
            result = ShouldUseApi(options)
                ? await GenerateApiMessagesAsync(messages, options)
                : await GenerateLocalMessagesAsync(messages, options);
        }
        finally
        {
            _gate.Release();
        }

        sw.Stop();
        if (result is not null)
            Log.Info($"[LLM] ✓ Ответ ИИ получен ({result.Length} симв., {sw.ElapsedMilliseconds} мс)");
        else
            Log.Info("[LLM] ✗ ИИ не ответил (причина в ошибке выше)");

        return result;
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(IReadOnlyList<object> messages, LlmRequestOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages.Count == 0)
        {
            Log.Info("[LLM] ✗ Streaming: пустой messages");
            yield break;
        }

        _logger?.LogInformation("[LLM] Streaming request (messages): provider={Provider} model={Model} count={Count}", ShouldUseApi(options) ? "api" : "local", options?.Model ?? CurrentModel, messages.Count);

        await _gate.WaitAsync(ct);
        try
        {
            await WaitForPacingAsync(ShouldUseApi(options));
            if (ShouldUseApi(options))
            {
                await foreach (var chunk in StreamApiMessagesAsync(messages, options, ct))
                    yield return chunk;
            }
            else
            {
                await foreach (var chunk in StreamLocalMessagesAsync(messages, options, ct))
                    yield return chunk;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Ok, long Ms, string Model)> PingAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await WaitForPacingAsync(_settings.Provider == "api");
            var sw = Stopwatch.StartNew();
            string? result;
            if (_settings.Provider == "api")
            {
                result = await GenerateApiAsync("ping", new LlmRequestOptions(Temperature: 0, NumPredict: 1, Format: null, TimeoutSeconds: 20, MaxRetries: 0));
            }
            else
            {
                result = await GenerateLocalAsync("ping", new LlmRequestOptions(Temperature: 0, NumPredict: 1, Format: null, TimeoutSeconds: 20, MaxRetries: 0));
            }
            return (result is not null, sw.ElapsedMilliseconds, CurrentModel);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Ok, long Ms, string Model)> PingAsync(string? provider, string model)
    {
        if (provider == "api" && string.IsNullOrWhiteSpace(_settings.Api.Key))
            return (false, 0, model);
        if (provider == "api" && IsApiModelBlocked(model))
        {
            _logger?.LogInformation("[LLM] Модель {Model} уже отклонена провайдером, проверку пропускаю", model);
            return (false, 0, model);
        }

        await _gate.WaitAsync();
        try
        {
            await WaitForPacingAsync(provider == "api");
            var sw = Stopwatch.StartNew();
            string? result;
            if (provider == "api")
            {
                result = await GenerateApiAsync("ping", new LlmRequestOptions(Temperature: 0, NumPredict: 1, Format: null, TimeoutSeconds: 20, MaxRetries: 0, Model: model, UseApi: true));
            }
            else
            {
                result = await GenerateLocalAsync("ping", new LlmRequestOptions(Temperature: 0, NumPredict: 1, Format: null, TimeoutSeconds: 20, MaxRetries: 0, Model: model, UseApi: false));
            }
            return (result is not null, sw.ElapsedMilliseconds, model);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(string prompt, LlmRequestOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Log.Info("[LLM] ✗ Streaming: пустой prompt");
            yield break;
        }

        _logger?.LogInformation("[LLM] Streaming request: provider={Provider} model={Model} len={Len}", _settings.Provider == "api" ? "api" : "local", CurrentModel, prompt.Length);

        await _gate.WaitAsync(ct);
        try
        {
            await WaitForPacingAsync(ShouldUseApi(options));
            if (ShouldUseApi(options))
            {
                await foreach (var chunk in StreamApiAsync(prompt, options, ct))
                    yield return chunk;
            }
            else
            {
                await foreach (var chunk in StreamLocalAsync(prompt, options, ct))
                    yield return chunk;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async IAsyncEnumerable<string> StreamLocalAsync(string prompt, LlmRequestOptions? options, [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? _settings.EffectiveLocalModel,
            ["prompt"] = prompt,
            ["stream"] = true,
            ["options"] = new
            {
                temperature = options?.Temperature ?? 0.1,
                num_predict = options?.NumPredict ?? 256
            }
        };

        var body = JsonSerializer.Serialize(request);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, _settings.EffectiveLocalUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("[LLM] Streaming HTTP {StatusCode}", response.StatusCode);
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await ReadLineSafeAsync(reader, ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parsed = TryParseOllamaChunk(line);
            if (parsed.Done) yield break;
            if (parsed.Error is not null)
            {
                _logger?.LogError("[LLM] Streaming Ollama error: {Error}", parsed.Error);
                yield break;
            }
            if (parsed.Text is not null)
                yield return parsed.Text;
        }
    }

    private async IAsyncEnumerable<string> StreamApiAsync(string prompt, LlmRequestOptions? options, [EnumeratorCancellation] CancellationToken ct)
    {
        var baseUrl = _settings.Api.BaseUrl.TrimEnd('/');
        var url = baseUrl + "/chat/completions";

        var requestModel = options?.Model ?? _settings.Api.Model;
        var request = new Dictionary<string, object>
        {
            ["model"] = requestModel,
            ["messages"] = new[]
            {
                new { role = "user", content = prompt }
            },
            ["temperature"] = options?.Temperature ?? 0.1,
            ["max_tokens"] = options?.NumPredict ?? 256,
            ["stream"] = true
        };

        var body = JsonSerializer.Serialize(request);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Authorization = new("Bearer", _settings.Api.Key);

        using var response = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("[LLM] Streaming API HTTP {StatusCode}", response.StatusCode);
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await ReadLineSafeAsync(reader, ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..];
            if (json == "[DONE]") yield break;

            var parsed = TryParseApiStreamChunk(json);
            if (parsed.Error is not null)
            {
                _logger?.LogError("[LLM] Streaming API error: {Error}", parsed.Error);
                yield break;
            }
            if (parsed.Text is not null)
                yield return parsed.Text;
        }
    }

    private static async Task<string?> ReadLineSafeAsync(StreamReader reader, CancellationToken ct)
    {
        try { return await reader.ReadLineAsync(ct); }
        catch { return null; }
    }

    private static (string? Text, string? Error, bool Done) TryParseOllamaChunk(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return (null, err.GetString(), false);
            if (doc.RootElement.TryGetProperty("done", out var done) && done.GetBoolean())
                return (null, null, true);
            if (doc.RootElement.TryGetProperty("response", out var resp))
            {
                var text = resp.GetString();
                if (text is not null) return (text, null, false);
            }
        }
        catch (JsonException) { }
        return (null, null, false);
    }

    private static (string? Text, string? Error) TryParseApiStreamChunk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return (null, err.GetString());
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                {
                    var text = content.GetString();
                    if (text is not null) return (text, null);
                }
            }
        }
        catch (JsonException) { }
        return (null, null);
    }

    // Ждём паузы между запросами (minInterval) и кулдауна после 429 (только api).
    private async Task WaitForPacingAsync(bool isApi)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var next = Volatile.Read(ref _nextAllowedTicks);
        if (isApi)
        {
            var cooldown = Volatile.Read(ref _cooldownUntilTicks);
            if (cooldown > next) next = cooldown;
        }
        if (next > nowTicks)
        {
            var wait = TimeSpan.FromTicks(next - nowTicks);
            if (wait.TotalMilliseconds > 100)
                _logger?.LogInformation("[LLM] Pace: жду {WaitMs} мс", wait.TotalMilliseconds);
            await Task.Delay(wait);
        }
        var minInterval = isApi ? _settings.Api.MinIntervalMs : _settings.Local.MinIntervalMs;
        Volatile.Write(ref _nextAllowedTicks, DateTime.UtcNow.Ticks + TimeSpan.FromMilliseconds(Math.Max(0, minInterval)).Ticks);
    }

    // ── Local: Ollama /api/generate ──
    private async Task<string?> GenerateLocalAsync(string prompt, LlmRequestOptions? options)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? _settings.EffectiveLocalModel,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["options"] = new
            {
                temperature = options?.Temperature ?? 0.1,
                num_predict = options?.NumPredict ?? 256
            }
        };
        if (options?.Format == "json")
            request["format"] = "json";

        return await PostAndParseAsync(
            _settings.EffectiveLocalUrl,
            JsonSerializer.Serialize(request),
            options,
            extractText: doc =>
            {
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new LlmError($"Ollama: {err.GetString()}");
                if (!doc.RootElement.TryGetProperty("response", out var responseElement))
                    throw new LlmError("Ollama: ответ не содержит поля response");
                return responseElement.GetString();
            });
    }

    // ── Api: OpenAI-совместимый chat/completions ──
    private async Task<string?> GenerateApiAsync(string prompt, LlmRequestOptions? options)
    {
        var baseUrl = _settings.Api.BaseUrl.TrimEnd('/');
        var url = baseUrl + "/chat/completions";

        var requestModel = options?.Model ?? _settings.Api.Model;
        var request = new Dictionary<string, object>
        {
            ["model"] = requestModel,
            ["messages"] = new[]
            {
                new { role = "user", content = prompt }
            },
            ["temperature"] = options?.Temperature ?? 0.1,
            ["max_tokens"] = options?.NumPredict ?? 256,
            ["stream"] = false
        };
        if (options?.Format == "json")
            request["response_format"] = new { type = "json_object" };

        var body = JsonSerializer.Serialize(request);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var maxRetries = options?.MaxRetries ?? _settings.Api.MaxRetries;
        var timeoutSeconds = options?.TimeoutSeconds ?? 120;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                httpReq.Headers.Authorization = new("Bearer", _settings.Api.Key);
                using var response = await _httpClient.SendAsync(httpReq, cts.Token);
                var responseText = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode && IsModelUnsupportedError(responseText) && requestModel != _settings.Api.Model)
                {
                    var attemptedModel = requestModel;
                    BlockApiModel(attemptedModel);
                    requestModel = _settings.Api.Model;
                    request["model"] = requestModel;
                    body = JsonSerializer.Serialize(request);
                    content = new StringContent(body, Encoding.UTF8, "application/json");
                    _logger?.LogWarning("[LLM] Модель {Model} не поддерживается провайдером, повторю с дефолтной {Default}", attemptedModel, _settings.Api.Model);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && options?.Format == "json")
                {
                    _logger?.LogInformation("[LLM] API не поддерживает json_object, повторяю без response_format");
                    var fallback = new Dictionary<string, object>(request);
                    fallback.Remove("response_format");
                    var fallbackContent = new StringContent(JsonSerializer.Serialize(fallback), Encoding.UTF8, "application/json");
                    using var httpReq2 = new HttpRequestMessage(HttpMethod.Post, url) { Content = fallbackContent };
                    httpReq2.Headers.Authorization = new("Bearer", _settings.Api.Key);
                    using var response2 = await _httpClient.SendAsync(httpReq2, cts.Token);
                    var responseText2 = await response2.Content.ReadAsStringAsync(cts.Token);
                    if (!response2.IsSuccessStatusCode)
                    {
                        _logger?.LogError("[LLM] API HTTP {StatusCode}: {Response}", response2.StatusCode, Truncate(responseText2));
                        return null;
                    }
                    return ParseApiContent(responseText2);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var backoff = Math.Max(1000, _settings.Api.Backoff429Ms);
                    Volatile.Write(ref _cooldownUntilTicks, DateTime.UtcNow.Ticks + TimeSpan.FromMilliseconds(backoff).Ticks);
                    _logger?.LogError("[LLM] API 429 — кулдаун {Backoff} с: {Response}", backoff / 1000, Truncate(responseText));
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (IsModelUnsupportedError(responseText))
                        BlockApiModel(requestModel);
                    _logger?.LogError("[LLM] API HTTP {StatusCode} (попытка {Attempt}/{MaxRetries}): {Response}", response.StatusCode, attempt + 1, maxRetries + 1, Truncate(responseText));
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * (attempt + 1));
                        continue;
                    }
                    return null;
                }

                return ParseApiContent(responseText);
            }
            catch (TaskCanceledException)
            {
                _logger?.LogError("[LLM] Timeout ({Timeout}с, попытка {Attempt}/{MaxRetries})", timeoutSeconds, attempt + 1, maxRetries + 1);
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogError(ex, "[LLM] Network error (попытка {Attempt}/{MaxRetries})", attempt + 1, maxRetries + 1);
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                return null;
            }
            catch (JsonException)
            {
                _logger?.LogError("[LLM] parse error: невалидный JSON в ответе");
                return null;
            }
        }

        return null;
    }

    private static string? ParseApiContent(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        if (doc.RootElement.TryGetProperty("error", out var errorElement))
            throw new LlmError($"API: {errorElement.GetString()}");
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new LlmError("API: ответ не содержит choices");
        var message = choices[0].GetProperty("message");
        return message.GetProperty("content").GetString();
    }

    // ── Local: Ollama /api/chat (messages) ──
    private async Task<string?> GenerateLocalMessagesAsync(IReadOnlyList<object> messages, LlmRequestOptions? options)
    {
        var chatUrl = _settings.EffectiveLocalUrl.Replace("/api/generate", "/api/chat");
        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? _settings.EffectiveLocalModel,
            ["messages"] = messages,
            ["stream"] = false,
            ["options"] = new
            {
                temperature = options?.Temperature ?? 0.1,
                num_predict = options?.NumPredict ?? 256
            }
        };

        return await PostAndParseAsync(
            chatUrl,
            JsonSerializer.Serialize(request),
            options,
            extractText: doc =>
            {
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new LlmError($"Ollama: {err.GetString()}");
                if (!doc.RootElement.TryGetProperty("message", out var msg))
                    throw new LlmError("Ollama: ответ не содержит message");
                if (msg.TryGetProperty("content", out var content))
                    return content.GetString();
                throw new LlmError("Ollama: message не содержит content");
            });
    }

    private async IAsyncEnumerable<string> StreamLocalMessagesAsync(IReadOnlyList<object> messages, LlmRequestOptions? options, [EnumeratorCancellation] CancellationToken ct)
    {
        var chatUrl = _settings.EffectiveLocalUrl.Replace("/api/generate", "/api/chat");
        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? _settings.EffectiveLocalModel,
            ["messages"] = messages,
            ["stream"] = true,
            ["options"] = new
            {
                temperature = options?.Temperature ?? 0.1,
                num_predict = options?.NumPredict ?? 256
            }
        };

        var body = JsonSerializer.Serialize(request);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, chatUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("[LLM] Streaming HTTP {StatusCode}", response.StatusCode);
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await ReadLineSafeAsync(reader, ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parsed = TryParseOllamaChunk(line);
            if (parsed.Done) yield break;
            if (parsed.Error is not null)
            {
                _logger?.LogError("[LLM] Streaming Ollama error: {Error}", parsed.Error);
                yield break;
            }
            if (parsed.Text is not null)
                yield return parsed.Text;
        }
    }

    // ── Api: OpenAI-совместимый chat/completions (messages) ──
    private async Task<string?> GenerateApiMessagesAsync(IReadOnlyList<object> messages, LlmRequestOptions? options)
    {
        var baseUrl = _settings.Api.BaseUrl.TrimEnd('/');
        var url = baseUrl + "/chat/completions";

        var requestModel = options?.Model ?? _settings.Api.Model;
        var request = new Dictionary<string, object>
        {
            ["model"] = requestModel,
            ["messages"] = messages,
            ["temperature"] = options?.Temperature ?? 0.1,
            ["max_tokens"] = options?.NumPredict ?? 256,
            ["stream"] = false
        };
        if (options?.Format == "json")
            request["response_format"] = new { type = "json_object" };

        var body = JsonSerializer.Serialize(request);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var maxRetries = options?.MaxRetries ?? _settings.Api.MaxRetries;
        var timeoutSeconds = options?.TimeoutSeconds ?? 120;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                httpReq.Headers.Authorization = new("Bearer", _settings.Api.Key);
                using var response = await _httpClient.SendAsync(httpReq, cts.Token);
                var responseText = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode && IsModelUnsupportedError(responseText) && requestModel != _settings.Api.Model)
                {
                    var attemptedModel = requestModel;
                    BlockApiModel(attemptedModel);
                    requestModel = _settings.Api.Model;
                    request["model"] = requestModel;
                    body = JsonSerializer.Serialize(request);
                    content = new StringContent(body, Encoding.UTF8, "application/json");
                    _logger?.LogWarning("[LLM] Модель {Model} не поддерживается провайдером, повторю с дефолтной {Default}", attemptedModel, _settings.Api.Model);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && options?.Format == "json")
                {
                    _logger?.LogInformation("[LLM] API не поддерживает json_object, повторяю без response_format");
                    var fallback = new Dictionary<string, object>(request);
                    fallback.Remove("response_format");
                    var fallbackContent = new StringContent(JsonSerializer.Serialize(fallback), Encoding.UTF8, "application/json");
                    using var httpReq2 = new HttpRequestMessage(HttpMethod.Post, url) { Content = fallbackContent };
                    httpReq2.Headers.Authorization = new("Bearer", _settings.Api.Key);
                    using var response2 = await _httpClient.SendAsync(httpReq2, cts.Token);
                    var responseText2 = await response2.Content.ReadAsStringAsync(cts.Token);
                    if (!response2.IsSuccessStatusCode)
                    {
                        _logger?.LogError("[LLM] API HTTP {StatusCode}: {Response}", response2.StatusCode, Truncate(responseText2));
                        return null;
                    }
                    return ParseApiContent(responseText2);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var backoff = Math.Max(1000, _settings.Api.Backoff429Ms);
                    Volatile.Write(ref _cooldownUntilTicks, DateTime.UtcNow.Ticks + TimeSpan.FromMilliseconds(backoff).Ticks);
                    _logger?.LogError("[LLM] API 429 — кулдаун {Backoff} с: {Response}", backoff / 1000, Truncate(responseText));
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (IsModelUnsupportedError(responseText))
                        BlockApiModel(requestModel);
                    _logger?.LogError("[LLM] API HTTP {StatusCode} (попытка {Attempt}/{MaxRetries}): {Response}", response.StatusCode, attempt + 1, maxRetries + 1, Truncate(responseText));
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * (attempt + 1));
                        continue;
                    }
                    return null;
                }

                return ParseApiContent(responseText);
            }
            catch (TaskCanceledException)
            {
                _logger?.LogError("[LLM] Timeout ({Timeout}с, попытка {Attempt}/{MaxRetries})", timeoutSeconds, attempt + 1, maxRetries + 1);
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogError(ex, "[LLM] Network error (попытка {Attempt}/{MaxRetries})", attempt + 1, maxRetries + 1);
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                return null;
            }
            catch (JsonException)
            {
                _logger?.LogError("[LLM] parse error: невалидный JSON в ответе");
                return null;
            }
        }

        return null;
    }

    private async IAsyncEnumerable<string> StreamApiMessagesAsync(IReadOnlyList<object> messages, LlmRequestOptions? options, [EnumeratorCancellation] CancellationToken ct)
    {
        var baseUrl = _settings.Api.BaseUrl.TrimEnd('/');
        var url = baseUrl + "/chat/completions";

        var requestModel = options?.Model ?? _settings.Api.Model;
        var request = new Dictionary<string, object>
        {
            ["model"] = requestModel,
            ["messages"] = messages,
            ["temperature"] = options?.Temperature ?? 0.1,
            ["max_tokens"] = options?.NumPredict ?? 256,
            ["stream"] = true
        };

        var body = JsonSerializer.Serialize(request);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Authorization = new("Bearer", _settings.Api.Key);

        using var response = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("[LLM] Streaming API HTTP {StatusCode}", response.StatusCode);
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await ReadLineSafeAsync(reader, ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..];
            if (json == "[DONE]") yield break;

            var parsed = TryParseApiStreamChunk(json);
            if (parsed.Error is not null)
            {
                _logger?.LogError("[LLM] Streaming API error: {Error}", parsed.Error);
                yield break;
            }
            if (parsed.Text is not null)
                yield return parsed.Text;
        }
    }

    // ── Общий retry-цикл для Ollama-транспорта (у API свой из-за Bearer/fallback) ──
    private async Task<string?> PostAndParseAsync(string url, string jsonBody, LlmRequestOptions? options, Func<JsonDocument, string?> extractText)
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var maxRetries = options?.MaxRetries ?? _settings.Local.MaxRetries;
        var timeoutSeconds = options?.TimeoutSeconds ?? 120;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var response = await _httpClient.PostAsync(url, content, cts.Token);
                var responseText = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound && url.Contains("/api/"))
                    {
                        var available = await GetOllamaAvailableModelsAsync();
                        var model = _settings.EffectiveLocalModel;
                        if (available is not null)
                            _logger?.LogError("[LLM] Модель {Model} не установлена. Доступные модели: {Available}", model, string.Join(", ", available));
                        else
                            _logger?.LogError("[LLM] Модель {Model} не установлена или Ollama недоступна ({Url})", model, _settings.EffectiveLocalUrl);
                    }
                    _logger?.LogError("[LLM] HTTP {StatusCode} (попытка {Attempt}/{MaxRetries})", response.StatusCode, attempt + 1, maxRetries + 1);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * (attempt + 1));
                        continue;
                    }
                    return null;
                }

                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    var result = extractText(doc);
                    _logger?.LogInformation("[LLM] Response: len={Len}", result?.Length ?? 0);
                    return result;
                }
                catch (LlmError ex)
                {
                    _logger?.LogError(ex, "[LLM] {Message}", ex.Message);
                    return null;
                }
                catch (JsonException)
                {
                    _logger?.LogError("[LLM] parse error: невалидный JSON в ответе");
                    return null;
                }
            }
            catch (TaskCanceledException)
            {
                _logger?.LogError("[LLM] Timeout ({Timeout}с, попытка {Attempt}/{MaxRetries})", timeoutSeconds, attempt + 1, maxRetries + 1);
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogError(ex, "[LLM] Network error (попытка {Attempt}/{MaxRetries})", attempt + 1, maxRetries + 1);
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                return null;
            }
        }

        return null;
    }

    private static string Truncate(string s, int max = 300) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");

    private static bool IsModelUnsupportedError(string? text) =>
        !string.IsNullOrWhiteSpace(text) && (
            text.Contains("model not supported", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("model is not supported", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("model not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("unsupported_model", StringComparison.OrdinalIgnoreCase));

    private async Task<List<string>?> GetOllamaAvailableModelsAsync()
    {
        try
        {
            var tagsUrl = _settings.EffectiveLocalUrl
                .Replace("/api/generate", "/api/tags")
                .Replace("/api/chat", "/api/tags");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _httpClient.GetAsync(tagsUrl, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("models", out var models)) return null;
            var list = new List<string>();
            foreach (var m in models.EnumerateArray())
            {
                if (m.TryGetProperty("name", out var name))
                    list.Add(name.GetString() ?? "?");
            }
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    public async Task DiagnoseLocalAsync()
    {
        if (IsApi) return;
        var model = _settings.EffectiveLocalModel;
        var available = await GetOllamaAvailableModelsAsync();
        if (available is null)
        {
            Log.Info($"[LLM] ⚠ Ollama недоступен на {_settings.EffectiveLocalUrl}. Проверьте, что Ollama запущена (модель {model}).");
            return;
        }
        if (available.Contains(model))
        {
            Log.Info($"[LLM] ✓ Локальная модель {model} доступна. Ollama: {_settings.EffectiveLocalUrl}");
        }
        else
        {
            Log.Info($"[LLM] ✗ Модель {model} НЕ установлена. Доступные модели: {string.Join(", ", available)}");
        }
    }

    // ── Public API for LlmRouter ──────────────────────────────────────

    public async Task<List<string>?> GetAvailableModelsAsync()
    {
        if (IsApi) return null;
        return await GetOllamaAvailableModelsAsync();
    }

    public async Task<List<string>?> GetAvailableLocalModelsAsync()
    {
        return await GetOllamaAvailableModelsAsync();
    }

    public async Task<List<string>?> GetAvailableApiModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.Api.Key)) return null;
        try
        {
            var url = _settings.Api.BaseUrl.TrimEnd('/') + "/models";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var httpReq = new HttpRequestMessage(HttpMethod.Get, url);
            httpReq.Headers.Authorization = new("Bearer", _settings.Api.Key);
            using var response = await _httpClient.SendAsync(httpReq, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            var ids = new List<string>();
            foreach (var el in data.EnumerateArray())
            {
                if (el.TryGetProperty("id", out var idEl))
                {
                    var id = idEl.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                }
            }
            return ids.Count == 0 ? null : ids;
        }
        catch { return null; }
    }

    public async Task<string?> GenerateVisionAsync(string prompt, string imageBase64, LlmRequestOptions? options = null, string? mime = "image/jpeg")
    {
        var messages = BuildVisionMessages(prompt, imageBase64, options, mime);
        return await GenerateAsync(messages, options);
    }

    public async IAsyncEnumerable<string> GenerateVisionStreamingAsync(string prompt, string imageBase64, LlmRequestOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default, string? mime = "image/jpeg")
    {
        var messages = BuildVisionMessages(prompt, imageBase64, options, mime);
        await foreach (var chunk in GenerateStreamingAsync(messages, options, ct))
            yield return chunk;
    }

    private static List<object> BuildVisionMessages(string prompt, string imageBase64, LlmRequestOptions? options, string? mime)
    {
        if (options?.UseApi == false)
        {
            return new List<object>
            {
                new { role = "user", content = prompt, images = new[] { imageBase64 } }
            };
        }

        return new List<object>
        {
            new { role = "user", content = new object[]
            {
                new { type = "text", text = prompt },
                new { type = "image_url", image_url = new { url = $"data:{mime ?? "image/jpeg"};base64,{imageBase64}" } }
            }}
        };
    }
}

public sealed record LlmRequestOptions(
    double Temperature = 0.1,
    int NumPredict = 256,
    string? Format = null,
    int TimeoutSeconds = 120,
    int? MaxRetries = null,
    string? Model = null,
    bool? UseApi = null);

internal sealed class LlmError : Exception
{
    public LlmError(string message) : base(message) { }
}
