using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class ToolExecutor
{
    private readonly ToolRegistry _registry;
    private readonly ILogger<ToolExecutor>? _logger;

    public ToolExecutor(ToolRegistry registry, ILogger<ToolExecutor>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

    public async Task<string?> ExecuteAsync(string functionName, Dictionary<string, JsonElement> args, string userInput, string? username = null)
    {
        _logger?.LogInformation("[TOOL] Execute: {Function} args={ArgCount}", functionName, args.Count);

        var tool = _registry.Find(functionName);
        if (tool is null)
        {
            _logger?.LogError("[TOOL] Not found: {Function}", functionName);
            return null;
        }

        if (tool.Method.GetCustomAttribute<RequireAdminAttribute>() is not null)
        {
            var currentUser = username ?? RequestContext.Current.Value;
            if (UserStore.Current is null || !UserStore.Current.IsAdmin(currentUser))
                return "Отказано: требуется права администратора.";
        }

        if (!TryBuildArguments(tool, args, userInput, out var values, out var error))
        {
            _logger?.LogError("[TOOL] Arg build error: {Error}", error);
            return error;
        }

        _logger?.LogInformation("[TOOL] Invoking: {ClassName}.{MethodName}", tool.Instance.GetType().Name, tool.Method.Name);

        try
        {
            var result = tool.Method.Invoke(tool.Instance, values);
            if (result is Task<string> task)
            {
                var str = await task;
                _logger?.LogInformation("[TOOL] Result: {Result}", Log.Truncate(str));
                return str;
            }

            if (result is Task t)
            {
                await t;
                _logger?.LogInformation("[TOOL] Completed (void)");
                return null;
            }

            _logger?.LogInformation("[TOOL] Result: {Result}", Log.Truncate(result?.ToString()));
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TOOL] Error");
            return $"Ошибка: {ex.Message}";
        }
    }

    private static bool TryBuildArguments(
        ToolDescriptor tool,
        Dictionary<string, JsonElement> args,
        string userInput,
        out object?[] values,
        out string error)
    {
        var parameters = tool.Method.GetParameters();
        values = new object?[parameters.Length];
        error = string.Empty;

        var argsLookup = new Dictionary<string, JsonElement>(args, StringComparer.OrdinalIgnoreCase);
        var ip = ExtractIp(userInput);

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var name = param.Name ?? $"arg{i}";
            var desc = tool.Parameters.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (argsLookup.TryGetValue(name, out var raw))
            {
                if (!TryConvert(raw, param.ParameterType, out var converted, out error))
                {
                    error = $"не удалось преобразовать аргумент '{name}': {error}";
                    return false;
                }
                if (!CheckRange(name, converted, desc, out error))
                    return false;
                values[i] = converted;
                continue;
            }

            if (IsUserInputParam(name))
            {
                values[i] = userInput;
                continue;
            }

            if (IsIpParam(name) && !string.IsNullOrWhiteSpace(ip))
            {
                values[i] = ip;
                continue;
            }

            if (param.HasDefaultValue)
            {
                values[i] = param.DefaultValue;
                continue;
            }

            if (IsNullable(param.ParameterType))
            {
                values[i] = null;
                continue;
            }

            error = $"обязательный аргумент '{name}' отсутствует.";
            return false;
        }

        return true;
    }

    private static bool CheckRange(string name, object? value, ToolParameterDescriptor? desc, out string error)
    {
        error = string.Empty;
        if (value is null || desc is null || (desc.Minimum is null && desc.Maximum is null))
            return true;

        var num = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var min = desc.Minimum is not null ? Convert.ToDouble(desc.Minimum, CultureInfo.InvariantCulture) : double.MinValue;
        var max = desc.Maximum is not null ? Convert.ToDouble(desc.Maximum, CultureInfo.InvariantCulture) : double.MaxValue;

        if (num < min || num > max)
        {
            error = $"аргумент '{name}' вне допустимого диапазона ({min:0.##}..{max:0.##}).";
            return false;
        }
        return true;
    }

    private static bool TryConvert(JsonElement value, Type targetType, out object? result, out string error)
    {
        error = string.Empty;
        result = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            if (IsNullable(targetType)) return true;
            error = "null недопустим для этого типа.";
            return false;
        }

        var nonNull = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNull == typeof(string))
        {
            result = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            return true;
        }

        if (nonNull == typeof(int))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            {
                result = intValue;
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue))
            {
                result = intValue;
                return true;
            }
            error = "ожидалось целое число.";
            return false;
        }

        if (nonNull == typeof(long))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
            {
                result = longValue;
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out longValue))
            {
                result = longValue;
                return true;
            }
            error = "ожидалось целое число.";
            return false;
        }

        if (nonNull == typeof(double))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var doubleValue))
            {
                result = doubleValue;
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out doubleValue))
            {
                result = doubleValue;
                return true;
            }
            error = "ожидалось число.";
            return false;
        }

        if (nonNull == typeof(float))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var floatValue))
            {
                result = floatValue;
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && float.TryParse(value.GetString(), out floatValue))
            {
                result = floatValue;
                return true;
            }
            error = "ожидалось число.";
            return false;
        }

        if (nonNull == typeof(bool))
        {
            if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            {
                result = value.GetBoolean();
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var boolValue))
            {
                result = boolValue;
                return true;
            }
            error = "ожидалось true/false.";
            return false;
        }

        if (nonNull.IsEnum)
        {
            if (value.ValueKind == JsonValueKind.String && Enum.TryParse(nonNull, value.GetString(), true, out var enumValue))
            {
                result = enumValue;
                return true;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var enumInt))
            {
                result = Enum.ToObject(nonNull, enumInt);
                return true;
            }
            error = "ожидалось значение enum.";
            return false;
        }

        error = $"тип {nonNull.Name} не поддерживается.";
        return false;
    }

    private static bool IsNullable(Type type)
    {
        return Nullable.GetUnderlyingType(type) is not null;
    }

    private static bool IsUserInputParam(string name)
    {
        return name.Equals("instruction", StringComparison.OrdinalIgnoreCase)
               || name.Equals("request", StringComparison.OrdinalIgnoreCase)
               || name.Equals("text", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIpParam(string name)
    {
        return name.Equals("ip", StringComparison.OrdinalIgnoreCase)
               || name.Equals("deviceIp", StringComparison.OrdinalIgnoreCase)
               || name.Equals("host", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractIp(string input)
    {
        var match = Regex.Match(input, @"\b(?:(?:25[0-5]|2[0-4]\d|1\d{2}|\d{1,2})\.){3}(?:25[0-5]|2[0-4]\d|1\d{2}|\d{1,2})\b");
        return match.Success ? match.Value : null;
    }
}
