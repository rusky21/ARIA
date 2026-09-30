using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class TtsService
{
    private const string DefaultVoice = "ru_RU-irina-medium";

    private readonly string _piperDir;
    private readonly string _binaryPath;
    private readonly string _voicesDir;
    private string _voicePath;

    private readonly TtsSettings? _settings;
    private readonly LlmSettings? _llm;

    private static readonly string[] WarmupPhrases =
    [
        "Слушаю вас.", "Готово.", "Не поняла.",
        "Открываю сетевой анализ.", "Открываю монитор системы.",
        "Открываю терминал.", "Открываю файлы и логи.",
        "Открываю задачи.", "Открываю настройки.", "Открываю чат.",
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private readonly ILogger<TtsService>? _logger;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    public static IReadOnlyList<string> ApiVoices { get; } = new[] { "alloy", "echo", "fable", "onyx", "nova", "shimmer" };

    public bool IsAvailable => File.Exists(_binaryPath) && File.Exists(_voicePath);

    public TtsService(TtsSettings? settings = null, LlmSettings? llmSettings = null, ILogger<TtsService>? logger = null, string? piperDir = null)
    {
        _settings = settings;
        _llm = llmSettings;
        _logger = logger;
        _piperDir = piperDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "piper");
        var binName = OperatingSystem.IsWindows() ? "piper.exe" : "piper";
        _binaryPath = Path.Combine(_piperDir, "bin", binName);
        _voicesDir = Path.Combine(_piperDir, "models");
        var startupVoice = ResolveVoiceName(_settings?.Voice);
        _voicePath = Path.Combine(_voicesDir, (startupVoice is not null && IsValidVoiceName(startupVoice) ? startupVoice : DefaultVoice) + ".onnx");
    }

    public string CurrentVoice()
    {
        if (File.Exists(_voicePath))
            return Path.GetFileNameWithoutExtension(_voicePath);
        return ResolveVoiceName(_settings?.Voice) ?? DefaultVoice;
    }

    public string[] ListLocalVoices()
    {
        try
        {
            if (!Directory.Exists(_voicesDir))
                return Array.Empty<string>();
            return Directory.GetFiles(_voicesDir, "*.onnx")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TTS] Failed to list voices");
            return Array.Empty<string>();
        }
    }

    public void SetVoice(string? voice)
    {
        if (string.IsNullOrWhiteSpace(voice))
        {
            _voicePath = Path.Combine(_voicesDir, DefaultVoice + ".onnx");
            return;
        }
        var stem = voice.Trim();
        if (!IsValidVoiceName(stem)) return;
        _voicePath = Path.Combine(_voicesDir, stem + ".onnx");
    }

    public string? ResolveVoiceName(string? voice)
    {
        return string.IsNullOrWhiteSpace(voice) ? DefaultVoice : voice.Trim();
    }

    public (bool Ok, string Message) CheckLocal(string? voice)
    {
        if (!File.Exists(_binaryPath))
            return (false, "Piper не установлен");
        var stem = string.IsNullOrWhiteSpace(voice) ? CurrentVoice() : voice.Trim();
        if (!File.Exists(Path.Combine(_voicesDir, stem + ".onnx")))
            return (false, "Голос не найден");
        return (true, "Готов");
    }

    public async Task WarmupAsync()
    {
        if (!IsAvailable) return;
        _logger?.LogInformation("[TTS] Warming up cache...");
        foreach (var phrase in WarmupPhrases)
        {
            if (_cache.ContainsKey(phrase)) continue;
            var wav = await SynthesizeInternalAsync(phrase, 1f);
            if (wav is not null)
                _cache[phrase] = wav;
        }
        _logger?.LogInformation("[TTS] Cache warmed: {Count} phrases", _cache.Count);
    }

    public async Task<byte[]?> SynthesizeAsync(string text, float rate = 1f, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.LogError("[TTS] Empty text");
            return null;
        }

        rate = Math.Clamp(rate, 0.5f, 1.5f);
        var cacheKey = $"{rate}|{text}";
        _logger?.LogInformation("[TTS] Synthesize (rate={Rate:F2}): {Text}", rate, text[..Math.Min(text.Length, 80)]);

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _logger?.LogInformation("[TTS] Cache hit: {Length} bytes", cached.Length);
            return cached;
        }

        var (wav, _) = await SynthesizeWithMessageAsync(text, rate, "local", _settings?.Voice, ct);
        _logger?.LogInformation("[TTS] Result: {Length} bytes", wav?.Length ?? 0);

        if (wav is not null && text.Length < 100)
            _cache[cacheKey] = wav;

        return wav;
    }

    public async Task<(byte[]? Wav, string? Error)> SynthesizeWithMessageAsync(string text, float rate, string? engine, string? voice, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "Пустой текст");

        if (string.Equals(engine, "api", StringComparison.OrdinalIgnoreCase))
        {
            if (_llm is null || string.IsNullOrWhiteSpace(_llm.Api.Key))
                return (null, "API-ключ не задан");
            var wav = await SynthesizeViaApiAsync(text, voice, rate, ct);
            return wav is null ? (null, "API-синтез недоступен") : (wav, null);
        }

        if (!File.Exists(_binaryPath) || !File.Exists(_voicePath))
            return (null, "Piper не установлен или голос не найден");

        rate = Math.Clamp(rate, 0.5f, 1.5f);
        var localWav = await SynthesizeInternalAsync(text, rate, ct, _voicePath);
        return localWav is null ? (null, "Синтез речи недоступен") : (localWav, null);
    }

    public async Task<byte[]?> SynthesizeViaApiAsync(string text, string? voice, float rate, CancellationToken ct = default)
    {
        if (_llm is null || string.IsNullOrWhiteSpace(_llm.Api.Key)) return null;
        try
        {
            var model = string.IsNullOrWhiteSpace(_llm.Api.Model) ? "" : _llm.Api.Model;
            if (!model.Contains("tts", StringComparison.OrdinalIgnoreCase))
                model = "openai/gpt-4o-mini-tts";

            var voiceName = string.IsNullOrWhiteSpace(voice)
                ? (string.IsNullOrWhiteSpace(_settings?.Voice) ? "alloy" : _settings!.Voice)
                : voice.Trim();
            if (!ApiVoices.Contains(voiceName))
                voiceName = "alloy";
            var speed = Math.Clamp(rate, 0.25f, 4.0f);

            var url = _llm.Api.BaseUrl.TrimEnd('/') + "/audio/speech";
            var payload = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["voice"] = voiceName,
                ["input"] = text,
                ["response_format"] = "wav",
                ["speed"] = speed
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new("Bearer", _llm.Api.Key);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = response.Content is not null ? await response.Content.ReadAsStringAsync(ct) : "";
                var trimmedBody = responseBody.Length <= 500 ? responseBody : responseBody[..500] + "…";
                _logger?.LogWarning("[TTS] API synth failed: {Status}: {Body}", response.StatusCode, trimmedBody);
                return null;
            }
            _logger?.LogInformation("[TTS] API synth: {Model} voice={Voice} len={Len}", model, voiceName, text.Length);
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TTS] API synth error");
            return null;
        }
    }

    private async Task<byte[]?> SynthesizeInternalAsync(string text, float rate = 1f, CancellationToken ct = default, string? modelPath = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var tempWav = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
            var usedModel = modelPath ?? _voicePath;

            var lengthScale = (1.0 / rate).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            var psi = new ProcessStartInfo
            {
                FileName = _binaryPath,
                Arguments = $"--model \"{usedModel}\" --length-scale {lengthScale} --output-file \"{tempWav}\"",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            process.StandardInput.Write(text);
            process.StandardInput.Close();

            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception killEx) { _logger?.LogError(killEx, "[TTS] Failed to kill process"); }
                return null;
            }

            if (process.ExitCode != 0) return null;
            if (!File.Exists(tempWav)) return null;

            var bytes = await File.ReadAllBytesAsync(tempWav, ct);
            try { File.Delete(tempWav); }
            catch (Exception ex) { _logger?.LogError(ex, "[TTS] Failed to delete temp file"); }
            return bytes;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TTS] Error");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValidVoiceName(string stem)
    {
        foreach (var ch in stem)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
                return false;
        }
        return true;
    }
}