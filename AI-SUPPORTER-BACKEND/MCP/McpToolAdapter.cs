using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI_Supporter;

internal sealed class McpToolAdapter : McpServerTool
{
    private readonly ToolDescriptor _descriptor;
    private readonly ToolRegistry _registry;

    public McpToolAdapter(ToolDescriptor descriptor, ToolRegistry registry)
    {
        _descriptor = descriptor;
        _registry = registry;
    }

    public override Tool ProtocolTool => BuildProtocolTool(_descriptor);

    public override IReadOnlyList<object> Metadata { get; } = [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var args = request.Params?.Arguments;
            var arguments = args is not null
                ? JsonSerializer.SerializeToElement(args)
                : default;

            var result = await _registry.ExecuteAsync(_descriptor.Name, arguments, cancellationToken);
            var text = result?.ToString() ?? "OK";

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = text }]
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = ex.Message }],
                IsError = true
            };
        }
    }

    private static Tool BuildProtocolTool(ToolDescriptor descriptor)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var p in descriptor.Parameters)
        {
            var prop = new JsonObject
            {
                ["type"] = GetJsonType(p.Type),
                ["description"] = p.Description
            };

            if (p.Minimum is not null)
                prop["minimum"] = Convert.ToDouble(p.Minimum);

            if (p.Maximum is not null)
                prop["maximum"] = Convert.ToDouble(p.Maximum);

            if (p.Type.IsEnum)
            {
                var vals = new JsonArray();
                foreach (var n in Enum.GetNames(p.Type))
                    vals.Add(n);
                prop["enum"] = vals;
            }

            properties[p.Name] = prop;

            if (p.Required)
                required.Add(p.Name);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
            schema["required"] = required;

        return new Tool
        {
            Name = descriptor.Name,
            Description = descriptor.Description,
            InputSchema = JsonSerializer.SerializeToElement(schema)
        };
    }

    private static string GetJsonType(Type type)
    {
        var nonNull = Nullable.GetUnderlyingType(type) ?? type;
        if (nonNull == typeof(string)) return "string";
        if (nonNull == typeof(int) || nonNull == typeof(long)) return "integer";
        if (nonNull == typeof(float) || nonNull == typeof(double)) return "number";
        if (nonNull == typeof(bool)) return "boolean";
        if (nonNull.IsEnum) return "string";
        return "string";
    }
}
