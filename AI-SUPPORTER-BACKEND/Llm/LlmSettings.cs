using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Supporter;

/// <summary>
/// Настройки LLM-провайдера. Хранятся в llm.json рядом с exe.
/// provider: "local" — Ollama, "api" — OpenAI-совместимый API (proxyapi / OpenRouter).
/// </summary>
public sealed class LlmSettings
{
    public const string DefaultFileName = "llm.json";

    public string Provider { get; set; } = "local"; // "local" | "api"
    public LocalLlmConfig Local { get; set; } = new();
    public ApiLlmConfig Api { get; set; } = new();

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, DefaultFileName);

    public static LlmSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<LlmSettings>(json, JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LLM] Ошибка чтения llm.json: {ex.Message}");
        }
        return new LlmSettings();
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
            Console.Error.WriteLine($"[LLM] Ошибка сохранения llm.json: {ex.Message}");
        }
    }

    public string EffectiveLocalUrl =>
        Environment.GetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL") ?? Local.Url;

    public string EffectiveLocalModel =>
        Environment.GetEnvironmentVariable("AI_SUPPORTER_LLM_MODEL") ?? Local.Model;

    private static readonly JsonSerializerOptions JsonOptions = AppJson.File;
}

public sealed class LocalLlmConfig
{
    public string Url { get; set; } = "http://localhost:11434/api/generate";
    public string Model { get; set; } = "qwen2.5:7b";
    public int MinIntervalMs { get; set; } = 300;
    public int MaxRetries { get; set; } = 2;
}

public sealed class ApiLlmConfig
{
    public string BaseUrl { get; set; } = "https://api.proxyapi.ru/openai/v1";
    public string Model { get; set; } = "openai/gpt-5-mini";
    public string Key { get; set; } = "";
    public int MinIntervalMs { get; set; } = 2500;
    public int Backoff429Ms { get; set; } = 60000;
    public int MaxRetries { get; set; } = 1;
}
