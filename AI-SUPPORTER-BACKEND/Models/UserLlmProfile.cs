using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Supporter;

public sealed class UserLlmProfile
{
    public string Username { get; set; } = "";

    public TaskModelSelection Chat { get; set; } = new();
    public TaskModelSelection Voice { get; set; } = new();
    public TaskModelSelection Vision { get; set; } = new();
    public TaskModelSelection ToolCalling { get; set; } = new();

    public SpeechSelection Speech { get; set; } = new();

    public string? LegacyProvider { get; set; }
}

public sealed class TaskModelSelection
{
    public string Provider { get; set; } = "local";
    public string Model { get; set; } = "";
    public bool AutoSelect { get; set; } = true;
}

public sealed class SpeechSelection
{
    public string Provider { get; set; } = "local";
    public string Model { get; set; } = "";
    public string Lang { get; set; } = "ru";
}

public enum LlmTask
{
    Chat,
    Voice,
    Vision,
    ToolCalling,
    FastToolCalling
}

internal static class UserLlmProfileJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(UserLlmProfile profile) => JsonSerializer.Serialize(profile, Options);

    public static UserLlmProfile? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<UserLlmProfile>(json, Options); }
        catch { return null; }
    }
}