using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace AI_Supporter;

public static class ModelToolAdapter
{
    private static readonly JsonSerializerOptions _json = AppJson.ApiCompact;

    public static string BuildToolsJson(IReadOnlyCollection<McpClientTool> tools)
    {
        var result = tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.JsonSchema
            }
        });
        return JsonSerializer.Serialize(result, _json);
    }
}
