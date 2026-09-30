using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class WhisperService : IAsyncDisposable
{
    private const string ModelBaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    private static readonly string CliName = OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli";
    private static readonly string BinaryUrl = GetBinaryUrl();
    private static readonly string NativeLibExt = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsLinux() ? ".so" : ".dylib";

    private readonly string _whisperDir;
    private string _modelPath;
    private readonly string _defaultModelName;
    private readonly string _binaryPath;
    private readonly LlmSettings? _settings;
    private string _loadedModelName = "";
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private volatile bool _ready;
    private readonly ILogger<WhisperService>? _logger;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    private static readonly string[] HallucinationPhrases =
    {
        "спасибо за просмотр",
        "спасибо что посмотрели",
        "подписывайтесь на канал",
        "субтитры сделал",
        "субтитры делал",
        "продолжение следует",
        "до свидания",
        "пока пока",
        "американское онлайновое телевидение",
        "привет всем привет",
    };

    public static string? CleanTranscription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        var lower = trimmed.ToLowerInvariant();
        foreach (var phrase in HallucinationPhrases)
        {
            if (lower.Contains(phrase))
                return null;
        }
        return trimmed;
    }

    public WhisperService(LlmSettings? settings = null, string? modelPath = null, ILogger<WhisperService>? logger = null, string? whisperDir = null)
    {
        _logger = logger;
        _settings = settings;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _whisperDir = whisperDir ?? Path.Combine(appData, "whisper");
        Directory.CreateDirectory(_whisperDir);

        _defaultModelName = Environment.GetEnvironmentVariable("AI_SUPPORTER_WHISPER_MODEL") ?? "ggml-base.bin";
        _modelPath = modelPath ?? Path.Combine(_whisperDir, _defaultModelName);
        _binaryPath = Path.Combine(_whisperDir, "bin", CliName);
    }

    private static string GetBinaryUrl()
    {
        if (OperatingSystem.IsWindows())
            return "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-x64.zip";
        if (OperatingSystem.IsLinux())
            return "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-linux-x64.zip";
        if (OperatingSystem.IsMacOS())
            return "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-macos-arm64.zip";
        return "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-x64.zip";
    }

    public async Task EnsureModelAsync(CancellationToken ct = default)
    {
        await InitForModelAsync(_defaultModelName, ct);
    }

    private async Task InitForModelAsync(string resolvedModelName, CancellationToken ct)
    {
        if (_ready && string.Equals(_loadedModelName, resolvedModelName, StringComparison.OrdinalIgnoreCase))
            return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_ready && string.Equals(_loadedModelName, resolvedModelName, StringComparison.OrdinalIgnoreCase))
                return;

            _modelPath = Path.Combine(_whisperDir, resolvedModelName);
            _ready = false;

            await EnsureModelFileAsync(ct);
            await EnsureBinaryAsync(ct);

            _loadedModelName = resolvedModelName;
            _ready = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private async Task EnsureModelFileAsync(CancellationToken ct)
    {
        if (File.Exists(_modelPath))
        {
            _logger?.LogInformation("[WHISPER] Model already exists: {Path}", _modelPath);
            return;
        }

        var modelName = Path.GetFileName(_modelPath);
        await DownloadAsync($"{ModelBaseUrl}/{modelName}", _modelPath, "model", ct);
        _logger?.LogInformation("[WHISPER] Model saved to: {Path}", _modelPath);
    }

    private async Task EnsureBinaryAsync(CancellationToken ct)
    {
        if (File.Exists(_binaryPath))
        {
            _logger?.LogInformation("[WHISPER] CLI already exists: {Path}", _binaryPath);
            return;
        }

        var binDir = Path.GetDirectoryName(_binaryPath)!;
        var archiveName = OperatingSystem.IsWindows() ? "whisper-bin-x64.zip"
            : OperatingSystem.IsLinux() ? "whisper-bin-linux-x64.zip"
            : "whisper-bin-macos-arm64.zip";
        var archivePath = Path.Combine(_whisperDir, archiveName);

        await DownloadAsync(BinaryUrl, archivePath, "whisper.cpp", ct);

        try
        {
            Directory.CreateDirectory(binDir);

            if (OperatingSystem.IsWindows())
            {
                using var archive = ZipFile.OpenRead(archivePath);
                foreach (var entry in archive.Entries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    if (name.Length == 0) continue;

                    var wanted = name.Equals(CliName, StringComparison.OrdinalIgnoreCase)
                                 || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                    if (!wanted) continue;

                    entry.ExtractToFile(Path.Combine(binDir, name), overwrite: true);
                }
            }
            else
            {
                // Linux/macOS: tar.gz or zip — extract binary + native libs
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = ZipFile.OpenRead(archivePath);
                    foreach (var entry in archive.Entries)
                    {
                        var name = Path.GetFileName(entry.FullName);
                        if (name.Length == 0) continue;

                        var wanted = name.Equals(CliName, StringComparison.OrdinalIgnoreCase)
                                     || name.EndsWith(NativeLibExt, StringComparison.OrdinalIgnoreCase);
                        if (!wanted) continue;

                        entry.ExtractToFile(Path.Combine(binDir, name), overwrite: true);
                    }
                }
                else
                {
                    // For tar.gz, use tar command
                    var psi = new ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"xzf \"{archivePath}\" -C \"{binDir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc is not null)
                    {
                        await proc.WaitForExitAsync(ct);
                    }
                }

                // Make executable on Unix
                if (!OperatingSystem.IsWindows() && File.Exists(_binaryPath))
                {
                    chmod755(_binaryPath);
                }
            }
        }
        finally
        {
            TryDelete(archivePath);
        }

        if (!File.Exists(_binaryPath))
        {
            throw new FileNotFoundException($"{CliName} not found inside {BinaryUrl}", _binaryPath);
        }

        _logger?.LogInformation("[WHISPER] CLI installed: {Path}", _binaryPath);
    }

    private void chmod755(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"755 \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[WHISPER] Failed to chmod {Path}", path);
        }
    }

    private async Task DownloadAsync(string url, string destination, string label, CancellationToken ct)
    {
        _logger?.LogInformation("[WHISPER] Downloading {Label} from {Url}", label, url);

        var tempPath = destination + ".tmp";

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(30);

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                var totalRead = 0L;
                var lastUpdate = DateTime.MinValue;

                while (true)
                {
                    var read = await contentStream.ReadAsync(buffer, ct);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;

                    if (totalBytes > 0 && (DateTime.UtcNow - lastUpdate).TotalMilliseconds >= 500)
                    {
                        var pct = (double)totalRead / totalBytes * 100;
                        Console.Write($"\r[WHISPER] Downloading {label}: {pct:0.0}%");
                        lastUpdate = DateTime.UtcNow;
                    }
                }
            }

            _logger?.LogInformation("[WHISPER] Download complete: {Label}", label);

            File.Move(tempPath, destination, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger?.LogError(ex, "[WHISPER] Download failed ({Label})", label);
            throw;
        }
    }

    public async Task<string?> TranscribeAsync(byte[] audioData, CancellationToken ct = default)
        => await TranscribeAsync(audioData, null, null, null, ct);

    public async Task<string?> TranscribeAsync(byte[] audioData, string? provider, string? model, string? lang, CancellationToken ct = default)
    {
        var (text, _) = await TranscribeWithMessageAsync(audioData, provider, model, lang, ct);
        return text;
    }

    public async Task<(string? Text, string? Error)> TranscribeWithMessageAsync(byte[] audioData, string? provider, string? model, string? lang, CancellationToken ct = default)
    {
        _logger?.LogInformation("[WHISPER] Transcribe: {Length} bytes", audioData.Length);

        if (string.Equals(provider, "api", StringComparison.OrdinalIgnoreCase))
        {
            if (_settings is null || string.IsNullOrWhiteSpace(_settings.Api.Key))
            {
                _logger?.LogWarning("[WHISPER] API ключ не задан — транскрипция пропущена");
                return (null, "API-ключ не задан — распознавание невозможно");
            }
            return await TranscribeViaApiAsync(audioData, model, lang, ct);
        }

        return await TranscribeViaLocalAsync(audioData, model, lang, ct);
    }

    private async Task<(string? Text, string? Error)> TranscribeViaLocalAsync(byte[] audioData, string? model, string? lang, CancellationToken ct)
    {
        var resolvedName = ResolveLocalModelFileName(model);

        if (!_ready || !string.Equals(_loadedModelName, resolvedName, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("[WHISPER] Switching model to {Model}", resolvedName);
            await InitForModelAsync(resolvedName, ct);
        }

        var samples = WakeDsp.DecodeWav16(audioData);
        var speech = samples.Length > 0 ? WakeDsp.ExtractSpeech(samples) : Array.Empty<float>();
        var wavToWrite = speech.Length > 0 && speech.Length < samples.Length
            ? EncodeWav16(speech)
            : audioData;

        var tempWav = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");

        try
        {
            await File.WriteAllBytesAsync(tempWav, wavToWrite, ct);

            var langClause = lang switch
            {
                "en" => " -l en",
                "ru" => " -l ru",
                _ => ""
            };

            var args = new[] { "-bs 5 -t 2", "-bs 2 -t 1" };
            string stdout = "";
            string error = "";
            int lastExitCode = 0;
            string? result = null;

            for (var attempt = 0; attempt < args.Length; attempt++)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _binaryPath,
                    Arguments = $"-m \"{_modelPath}\" -f \"{tempWav}\" -nt{langClause} {args[attempt]}",
                    WorkingDirectory = Path.GetDirectoryName(_binaryPath)!,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return (null, "Распознавание не удалось (whisper.cpp)");

                var processStdout = process.StandardOutput.ReadToEndAsync(ct);
                var processStderr = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                var attemptOutput = await processStdout;
                var attemptError = await processStderr;

                if (process.ExitCode == 0)
                {
                    stdout = attemptOutput;
                    error = attemptError;
                    result = CleanTranscription(stdout);
                    break;
                }

                error = attemptError;
                lastExitCode = process.ExitCode;
                _logger?.LogWarning("[WHISPER] CLI exited with code {ExitCode} (попытка {Attempt}/{MaxAttempts}): {Error}", process.ExitCode, attempt + 1, args.Length, error.Trim());
            }

            if (result is null)
            {
                _logger?.LogError("[WHISPER] CLI exited with code {ExitCode}: {Error}", lastExitCode, error.Trim());
                return (null, $"whisper.cpp не смог распознать аудио (код выхода {lastExitCode})");
            }

            _logger?.LogInformation("[WHISPER] Result: {Result}", result);
            return (result, null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger?.LogError(ex, "[WHISPER] Cannot run whisper CLI at {Path}", _binaryPath);
            _ready = false;
            return (null, "Распознавание не удалось (whisper.cpp)");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WHISPER] Transcribe error");
            return (null, "Распознавание не удалось (whisper.cpp)");
        }
        finally
        {
            TryDelete(tempWav);
        }
    }

    private async Task<(string? Text, string? Error)> TranscribeViaApiAsync(byte[] audioData, string? model, string? lang, CancellationToken ct)
    {
        try
        {
            var apiModel = string.IsNullOrWhiteSpace(model) ? "openai/gpt-4o-mini-transcribe" : model.Trim();
            var url = _settings!.Api.BaseUrl.TrimEnd('/') + "/audio/transcriptions";

            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(audioData);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(fileContent, "file", "voice.wav");
            content.Add(new StringContent(apiModel), "model");
            if (lang is "ru" or "en")
                content.Add(new StringContent(lang), "language");

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new("Bearer", _settings.Api.Key);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                var trimmedBody = responseBody.Length <= 500 ? responseBody : responseBody[..500] + "…";
                _logger?.LogWarning("[WHISPER] API transcribe failed: {Status}: {Body}", response.StatusCode, trimmedBody);
                return (null, $"API-транскрипция недоступна ({(int)response.StatusCode})");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                return (CleanTranscription(textEl.GetString()?.Trim()), null);
            return (null, "API-транскрипция недоступна");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[WHISPER] API transcribe error");
            return (null, "API-транскрипция недоступна");
        }
    }

    private string ResolveLocalModelFileName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return _defaultModelName;
        var name = model.Trim();
        if (name is "tiny" or "base" or "small" or "medium" or "large-v3")
            return $"ggml-{name}.bin";
        return name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ? name : name + ".bin";
    }

    public (bool Ok, string Engine, string Message) CheckEngine(string? provider, string? model)
    {
        if (string.Equals(provider, "api", StringComparison.OrdinalIgnoreCase))
        {
            var ok = _settings is not null && !string.IsNullOrWhiteSpace(_settings.Api.Key);
            return (ok, "API", ok ? "Доступен" : "API-ключ не задан");
        }

        var resolvedName = ResolveLocalModelFileName(model);
        var modelFilePath = Path.Combine(_whisperDir, resolvedName);
        if (!File.Exists(_binaryPath))
            return (false, "whisper.cpp", "Движок whisper.cpp не установлен");
        if (!File.Exists(modelFilePath))
            return (false, "whisper.cpp", "Модель не найдена");
        return (true, "whisper.cpp", "Готов");
    }

    private static byte[] EncodeWav16(float[] samples)
    {
        var buffer = new byte[44 + samples.Length * 2];
        BitConverter.GetBytes(0x46464952).CopyTo(buffer, 0);
        BitConverter.GetBytes(36 + samples.Length * 2).CopyTo(buffer, 4);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(buffer, 12);
        BitConverter.GetBytes(16).CopyTo(buffer, 16);
        BitConverter.GetBytes((short)1).CopyTo(buffer, 20);
        BitConverter.GetBytes((short)1).CopyTo(buffer, 22);
        BitConverter.GetBytes(WakeDsp.SampleRate).CopyTo(buffer, 24);
        BitConverter.GetBytes(WakeDsp.SampleRate * 2).CopyTo(buffer, 28);
        BitConverter.GetBytes((short)2).CopyTo(buffer, 32);
        BitConverter.GetBytes((short)16).CopyTo(buffer, 34);
        Encoding.ASCII.GetBytes("data").CopyTo(buffer, 36);
        BitConverter.GetBytes(samples.Length * 2).CopyTo(buffer, 40);

        for (var i = 0; i < samples.Length; i++)
        {
            var s = Math.Clamp(samples[i], -1f, 1f);
            BitConverter.GetBytes((short)(s < 0 ? s * 0x8000 : s * 0x7FFF)).CopyTo(buffer, 44 + i * 2);
        }
        return buffer;
    }

    private void TryDelete(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WHISPER] Failed to delete temp file");
        }
    }

    public ValueTask DisposeAsync()
    {
        _initGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
