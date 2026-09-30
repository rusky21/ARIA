using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public readonly record struct WakeCheckResult(
    bool IsHotword, float Score, float Threshold, float DurationSec, int Frames);

public sealed class WakeTrainResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public int Frames { get; init; }
    public float DurationSec { get; init; }
    public int TemplatesCount { get; init; }
}

/// <summary>
/// Не-нейросетевой wake-детектор: VAD → MFCC → DTW против шаблонов из wake.json.
/// Никакого Whisper/LLM в цепочке пробуждения.
/// </summary>
public sealed class WakeWordService
{
    public const int MaxTemplates = 3;
    private const int ScoreHistorySize = 3;

    private readonly WakeSettings _settings;
    private readonly ILogger<WakeWordService>? _logger;
    private DateTime _lastHitUtc = DateTime.MinValue;
    private readonly object _cooldownLock = new();
    private readonly Queue<float> _scoreHistory = new();

    public WakeWordService(WakeSettings settings, ILogger<WakeWordService>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public float Threshold => _settings.EffectiveThreshold;

    public bool Enabled => _settings.Enabled;

    public IReadOnlyList<List<List<float>>>? Templates => _settings.Templates;

    /// <summary>Совместимый вариант (WebHostBridge / старые вызывающие).</summary>
    public Task<bool> CheckAsync(byte[] audioData, CancellationToken ct = default)
        => Task.FromResult(Check(audioData, ct).IsHotword);

    public WakeCheckResult Check(byte[] audioData, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            _logger?.LogDebug("[WAKE] Детекция отключена, пропуск");
            return new WakeCheckResult(false, 0f, Threshold, 0f, 0);
        }

        var score = ScoreInternal(audioData, out var durationSec, out var frames);
        var threshold = Threshold;

        float smoothed;
        lock (_scoreHistory)
        {
            _scoreHistory.Enqueue(score);
            while (_scoreHistory.Count > ScoreHistorySize)
                _scoreHistory.Dequeue();
            smoothed = _scoreHistory.Average();
        }

        var hit = smoothed >= threshold;

        var suppressed = false;
        if (hit && IsCoolingDown())
        {
            hit = false;
            suppressed = true;
        }

        if (hit)
        {
            lock (_cooldownLock)
                _lastHitUtc = DateTime.UtcNow;
        }

        _logger?.LogDebug("[WAKE] score={Score:F3} smoothed={Smoothed:F3} threshold={Threshold:F3} => {Result}{Cooldown}",
            score, smoothed, threshold, hit ? "HIT" : "miss", suppressed ? " (кулдаун)" : "");
        return new WakeCheckResult(hit, smoothed, threshold, durationSec, frames);
    }

    public float Score(byte[] audioData) => ScoreInternal(audioData, out _, out _);

    private float ScoreInternal(byte[] audioData, out float durationSec, out int frames)
    {
        durationSec = 0f;
        frames = 0;

        var templates = _settings.Templates;
        if (templates is null || templates.Count == 0) return 0f;

        var samples = WakeDsp.DecodeWav16(audioData);
        if (samples.Length < WakeDsp.SampleRate) return 0f;

        var speech = WakeDsp.ExtractSpeech(samples, _settings.VadFloor);
        durationSec = (float)speech.Length / WakeDsp.SampleRate;
        if (speech.Length == 0 || durationSec < 0.25f || durationSec > 3.0f) return 0f;

        var mfcc = WakeDsp.NormalizeCmvn(WakeDsp.ComputeMfcc(speech));
        frames = mfcc.Length;
        if (mfcc.Length < 5) return 0f;

        var best = 0f;
        foreach (var template in templates)
        {
            if (template is null || template.Count < 8) continue;

            var tmpl = new float[template.Count][];
            for (var i = 0; i < template.Count; i++)
                tmpl[i] = template[i].ToArray();
            WakeDsp.NormalizeCmvn(tmpl);

            var dist = WakeDsp.DtwDistance(mfcc, tmpl);
            var norm = dist / (mfcc.Length + tmpl.Length);
            var s = 1f / (1f + norm);
            if (s > best) best = s;
        }
        return best;
    }

    private bool IsCoolingDown()
    {
        if (_settings.MinHitIntervalSec <= 0f) return false;
        lock (_cooldownLock)
            return DateTime.UtcNow - _lastHitUtc < TimeSpan.FromSeconds(_settings.MinHitIntervalSec);
    }

    /// <summary>Совместимый вариант (старые вызывающие).</summary>
    public bool TrainAsync(byte[] audioData) => Train(audioData).Ok;

    public WakeTrainResult Train(byte[] audioData)
    {
        var samples = WakeDsp.DecodeWav16(audioData);
        if (samples.Length < WakeDsp.SampleRate)
            return new WakeTrainResult { Error = "Слишком короткий сегмент (< 1 с)" };

        var speech = WakeDsp.ExtractSpeech(samples);
        var duration = (float)speech.Length / WakeDsp.SampleRate;
        if (speech.Length == 0 || duration < 0.25f || duration > 3.0f)
        {
            var msg = $"Неудачный сегмент ({duration:F2} с)";
            _logger?.LogWarning("[WAKE] Train: {Message}", msg);
            return new WakeTrainResult { Error = msg, DurationSec = duration };
        }

        var mfcc = WakeDsp.NormalizeCmvn(WakeDsp.ComputeMfcc(speech));
        if (mfcc.Length < 8)
            return new WakeTrainResult { Error = "Мало кадров MFCC" };

        var templates = _settings.Templates ??= new List<List<List<float>>>();
        if (templates.Count >= MaxTemplates)
            templates.RemoveAt(0);
        templates.Add(mfcc.Select(m => m.ToList()).ToList());
        _settings.Save();

        _logger?.LogInformation("[WAKE] Шаблон обучен ({Count}/{Max}): {Frames} кадров, {Duration:F2} с, порог={Threshold:F2}",
            templates.Count, MaxTemplates, mfcc.Length, duration, Threshold);
        return new WakeTrainResult { Ok = true, Frames = mfcc.Length, DurationSec = duration, TemplatesCount = templates.Count };
    }

    public void ClearTemplates()
    {
        _settings.Templates = null;
        _settings.Save();
        _logger?.LogInformation("[WAKE] Шаблоны очищены");
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _settings.Save();
        _logger?.LogInformation("[WAKE] Enabled={Enabled}", enabled);
    }

    public void SetThreshold(float threshold)
    {
        threshold = Math.Clamp(threshold, 0.05f, 0.9f);
        _settings.Threshold = threshold;
        _settings.Save();
        _logger?.LogInformation("[WAKE] Threshold={Threshold:F2}", threshold);
    }
}
