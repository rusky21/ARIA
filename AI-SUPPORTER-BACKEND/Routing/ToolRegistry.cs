using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI_Supporter;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDescriptor> _tools;
    private ToolExecutor? _executor;

    public ToolRegistry(IEnumerable<object> providers)
    {
        _tools = new Dictionary<string, ToolDescriptor>(StringComparer.OrdinalIgnoreCase);
        AddProviders(providers);
    }

    public void AddProviders(IEnumerable<object> providers)
    {
        foreach (var provider in providers)
            AddProvider(provider);
    }

    public void Rebuild(IEnumerable<object> providers)
    {
        _tools.Clear();
        AddProviders(providers);
    }

    public void AddProvider(object provider)
    {
        var type = provider.GetType();
        if (type.GetCustomAttribute<AiToolProviderAttribute>() is null)
            return;

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            var attr = method.GetCustomAttribute<AiToolAttribute>();
            if (attr is null)
                continue;

            var parameters = method.GetParameters()
                .Select(p =>
                {
                    var paramAttr = p.GetCustomAttribute<AiParameterAttribute>();
                    return new ToolParameterDescriptor(
                        p.Name ?? "arg",
                        p.ParameterType,
                        paramAttr?.Description ?? string.Empty,
                        paramAttr?.Required ?? !p.HasDefaultValue,
                        paramAttr?.Minimum,
                        paramAttr?.Maximum
                    );
                })
                .ToList();

            var descriptor = new ToolDescriptor(
                attr.Name,
                attr.Description,
                method,
                provider,
                parameters
            );

            _tools[descriptor.Name] = descriptor;
        }
    }

    public bool RemoveProvider(object provider)
    {
        var type = provider.GetType();
        if (type.GetCustomAttribute<AiToolProviderAttribute>() is null)
            return false;

        var removed = false;
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            var attr = method.GetCustomAttribute<AiToolAttribute>();
            if (attr is null)
                continue;

            removed |= _tools.Remove(attr.Name);
        }
        return removed;
    }

    public ToolDescriptor? Find(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IReadOnlyCollection<ToolDescriptor> GetAll() => _tools.Values;

    public async Task<object?> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                args[prop.Name] = prop.Value;
        }
        return await ExecuteAsync(toolName, args, string.Empty, null, ct);
    }

    public async Task<object?> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        string userInput, string? username = null, CancellationToken ct = default)
    {
        if (!_tools.TryGetValue(toolName, out _))
            return null;

        _executor ??= new ToolExecutor(this);
        return await _executor.ExecuteAsync(toolName, args, userInput, username);
    }

    public JsonNode BuildParameterSchema(ToolParameterDescriptor param)
    {
        var schema = new JsonObject
        {
            ["type"] = GetJsonType(param.Type),
            ["description"] = param.Description
        };

        if (param.Minimum is not null)
            schema["minimum"] = Convert.ToDouble(param.Minimum);

        if (param.Maximum is not null)
            schema["maximum"] = Convert.ToDouble(param.Maximum);

        if (param.Type.IsEnum)
        {
            var enumVals = new JsonArray();
            foreach (var name in Enum.GetNames(param.Type))
                enumVals.Add(name);
            schema["enum"] = enumVals;
        }

        return schema;
    }

    public string BuildToolsJson()
    {
        var tools = _tools.Values.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = new
                {
                    type = "object",
                    properties = t.Parameters.ToDictionary(
                        p => p.Name,
                        p => BuildPropertySchema(p)
                    ),
                    required = t.Parameters
                        .Where(p => p.Required)
                        .Select(p => p.Name)
                        .ToList()
                }
            }
        }).ToList();

        return JsonSerializer.Serialize(tools, _jsonOptions);
    }

    private static readonly JsonSerializerOptions _jsonOptions = AppJson.ApiCompact;

    private static object BuildPropertySchema(ToolParameterDescriptor param)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = GetJsonType(param.Type),
            ["description"] = param.Description
        };

        if (param.Minimum is not null)
            schema["minimum"] = Convert.ToDouble(param.Minimum);

        if (param.Maximum is not null)
            schema["maximum"] = Convert.ToDouble(param.Maximum);

        if (param.Type.IsEnum)
        {
            schema["enum"] = Enum.GetNames(param.Type).ToList();
        }

        return schema;
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
