using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Supporter;

/// <summary>
/// Настройки не-нейросетевого wake-детектора. Хранятся в wake.json рядом с exe.
/// Template — MFCC-последовательность слова активации, получается через POST /api/voice/wake/train.
/// </summary>
public sealed class WakeSettings
{
    public const string DefaultFileName = "wake.json";

    public float Threshold { get; set; } = 0.30f;
    public float MinHitIntervalSec { get; set; } = 2f;
    public bool Enabled { get; set; } = true;
    public float VadFloor { get; set; } = 0.015f;

    public WakeSettings() { }

    /// <summary>Шаблоны слова активации (до WakeWordService.MaxTemplates). Каждый — MFCC-последовательность.</summary>
    public List<List<List<float>>>? Templates { get; set; }

    /// <summary>Легаси-поле из старых wake.json; мигрируется в Templates при загрузке.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<List<float>>? Template { get; set; }

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, DefaultFileName);

    public float EffectiveThreshold =>
        float.TryParse(
            Environment.GetEnvironmentVariable("AI_SUPPORTER_WAKE_THRESHOLD"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var t)
            ? t
            : Threshold;

    public static WakeSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<WakeSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.MigrateLegacyTemplate();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WAKE] Ошибка чтения wake.json: {ex.Message}");
        }
        return new WakeSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WAKE] Ошибка сохранения wake.json: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = AppJson.File;

    private void MigrateLegacyTemplate()
    {
        if ((Templates is null || Templates.Count == 0) && Template is { Count: > 0 })
        {
            Templates = new List<List<List<float>>> { Template };
            Template = null;
            Save();
            Console.WriteLine("[WAKE] Легаси-шаблон перенесён в Templates");
        }
    }
}
