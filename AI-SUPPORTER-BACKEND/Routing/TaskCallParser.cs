using System.Text.Json;

namespace AI_Supporter;

internal static class TaskCallParser
{
    public static bool TryParse(string text, out TaskCall call, out string error)
    {
        call = TaskCall.Empty;
        error = string.Empty;

        if (!TryParseJson(text, out var root, out error))
        {
            return false;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            var first = root.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Undefined || first.ValueKind == JsonValueKind.Null)
            {
                error = "пустой массив в ответе.";
                return false;
            }

            root = first;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "ответ не является JSON-объектом.";
            return false;
        }

        var function = GetString(root, "function")
                       ?? GetString(root, "name")
                       ?? GetString(root, "tool");
        if (string.IsNullOrWhiteSpace(function))
        {
            error = "в ответе нет поля function.";
            return false;
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in argsElement.EnumerateObject())
            {
                args[prop.Name] = prop.Value;
            }
        }
        else
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.Equals("function", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.Equals("tool", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.Equals("message", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                args[prop.Name] = prop.Value;
            }
        }

        var message = GetString(root, "message");
        call = new TaskCall(function, args, message);
        return true;
    }

    private static bool TryParseJson(string text, out JsonElement root, out string error)
    {
        error = string.Empty;
        root = default;

        try
        {
            var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "не удалось найти JSON в ответе.";
                return false;
            }

            var slice = text[start..(end + 1)];
            try
            {
                var doc = JsonDocument.Parse(slice);
                root = doc.RootElement.Clone();
                return true;
            }
            catch (JsonException ex)
            {
                error = $"JSON невалиден: {ex.Message}";
                return false;
            }
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
