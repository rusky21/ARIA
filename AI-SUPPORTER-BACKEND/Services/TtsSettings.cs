using System.Text.Json;

namespace AI_Supporter;

public sealed class TtsSettings
{
    public string Engine { get; set; } = "local";
    public string Voice { get; set; } = "";
    public const string DefaultFileName = "tts.json";
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, DefaultFileName);

    public static TtsSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<TtsSettings>(json, AppJson.File);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TTS] Ошибка чтения tts.json: {ex.Message}");
        }
        return new TtsSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, AppJson.File);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TTS] Ошибка сохранения tts.json: {ex.Message}");
        }
    }
}