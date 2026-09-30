using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Supporter;

/// <summary>
/// Shared JsonSerializerOptions for the entire application.
/// </summary>
public static class AppJson
{
    /// <summary>For file persistence: CamelCase + indented + relaxed encoding.</summary>
    public static JsonSerializerOptions File { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>For API/WebSocket: default (secure) encoding, no indentation.</summary>
    public static JsonSerializerOptions Api { get; } = new()
    {
        Encoder = JavaScriptEncoder.Default
    };

    /// <summary>For API responses: default (secure) encoding + ignore nulls.</summary>
    public static JsonSerializerOptions ApiCompact { get; } = new()
    {
        Encoder = JavaScriptEncoder.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
