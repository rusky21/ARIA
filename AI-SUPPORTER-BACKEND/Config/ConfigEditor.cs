using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

/// <summary>
/// Редактор конфигурации устройства через LLM.
/// </summary>
public sealed class ConfigEditor
{
    // Базовые настройки редактирования конфигурации устройства.
    private const string DeviceBaseUrl = "http://192.168.1.132";
    private const string PromptFile = "prompt.txt";
    private const string LocalFallbackFile = "cfg.json";
    private const string OutputFile = "cfg_out.json";
    private const string LlmLogFile = "ollama.log";

    // Сетка для GUI (используется в командах перемещения).
    private const int GridXBase = 8;
    private const int GridXStep = 160;
    private const int GridXColumns = 5;
    private const int GridYBase = 3;
    private const int GridYStep = 120;
    private const int GridYRows = 3;

    private readonly LlmClient _llm;
    private readonly ILogger<ConfigEditor>? _logger;

    public ConfigEditor(LlmClient llm, ILogger<ConfigEditor>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    public Task EditAsync(string input) => ProcessInstructionWithLlmAsync(input);

    // ===== Основной процесс =====
    private async Task ProcessInstructionWithLlmAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var (deviceIp, instruction) = ExtractIpAndInstruction(input);

        if (string.IsNullOrWhiteSpace(instruction))
        {
            _logger?.LogError("Error: instruction is empty.");
            return;
        }

        var baseUrl = deviceIp is not null ? $"http://{deviceIp}" : DeviceBaseUrl;
        var sourceJsonUrl = baseUrl + "/gui_conf.json";
        var settingsUploadUrl = baseUrl + "/settings_file";

        string? jsonText = null;
        try
        {
            jsonText = await LoadJsonAsync(sourceJsonUrl);
        }
        catch (HttpRequestException ex) when (IsConnectionReset(ex))
        {
            _logger?.LogError("Warning: device connection was reset while loading JSON.");
            AppendLog($"Device connection reset: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Warning: failed to load JSON from device: {Message}", ex.Message);
            AppendLog($"Source JSON load failed: {ex.Message}");
        }

        if (!IsLikelyJson(jsonText ?? string.Empty))
        {
            _logger?.LogError("Warning: device response is not JSON, using local cfg.json.");
            jsonText = await LoadLocalFallbackAsync();
        }

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            _logger?.LogError("Error: source JSON is empty.");
            return;
        }

        var root = ParseRootObject(jsonText, out var hadDuplicates);
        if (root is null)
        {
            _logger?.LogError("Error: failed to parse source JSON.");
            return;
        }

        if (hadDuplicates)
        {
            AppendLog("Source JSON had duplicate keys; last wins.");
        }

        var indexResult = BuildElementIndex(root);
        var promptTemplate = await LoadPromptTemplateAsync();
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            return;
        }

        var elementsText = BuildElementsPayload(indexResult);
        var prompt = promptTemplate.Replace("{instruction}", instruction).Replace("{elements}", elementsText);

        var response = await _llm.GenerateAsync(prompt, new LlmRequestOptions(Temperature: 0.1, NumPredict: 256, Format: "json"));
        if (string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        if (!TryExtractJson(response, out var responseJson, out var comment, out var extractError))
        {
            _logger?.LogError("Error: {ExtractError}", extractError);
            AppendLog($"LLM extract error: {extractError}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            _logger?.LogError("{Comment}", comment);
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(responseJson);
        }
        catch (JsonException ex)
        {
            _logger?.LogError("Error: failed to parse LLM JSON.");
            AppendLog($"LLM JSON parse error: {ex.Message}");
            return;
        }

        using (parsed)
        {
            if (!TryGetActions(parsed.RootElement, out var actions, out var actionsError))
            {
                _logger?.LogError("Error: {ActionsError}", actionsError);
                AppendLog($"LLM actions error: {actionsError}");
                return;
            }

            var buttonDisplayIndices = ExtractButtonDisplayIndices(instruction);
            var forcedColor = ResolveNamedColorFromInstruction(instruction);
            var forcedAdjustment = ResolveAdjustmentFromInstruction(instruction);

            var applied = 0;
            foreach (var action in actions)
            {
                if (TryApplyLlmAction(action, indexResult, buttonDisplayIndices, forcedColor, forcedAdjustment, out var error, out var info))
                {
                    applied++;
                    if (!string.IsNullOrWhiteSpace(info))
                    {
                        _logger?.LogError("{Info}", info);
                    }
                }
                else
                {
                    _logger?.LogError("Error: {Error}", error);
                }
            }

            if (applied == 0)
            {
                _logger?.LogError("Error: no actions applied.");
                return;
            }
        }

        var outputJson = root.ToJsonString(AppJson.File);
        await File.WriteAllTextAsync(ResolveOutputPath(OutputFile), outputJson, GetUtf8NoBom());
        await UploadConfigAsync(settingsUploadUrl, outputJson);
    }

    // ===== Загрузка и сохранение =====
    private static (string? deviceIp, string instruction) ExtractIpAndInstruction(string input)
    {
        var match = Regex.Match(input, @"\b(?:(?:25[0-5]|2[0-4]\d|1\d{2}|\d{1,2})\.){3}(?:25[0-5]|2[0-4]\d|1\d{2}|\d{1,2})\b");
        if (match.Success)
        {
            var ip = match.Value;
            var instr = input[..match.Index] + input[(match.Index + match.Length)..];
            instr = Regex.Replace(instr, @"\s+", " ").Trim();
            return (ip, instr);
        }

        return (null, input.Trim());
    }

    private static async Task<string?> LoadPromptTemplateAsync()
    {
        var path = ResolveFallbackPath(PromptFile);
        if (path is null)
        {
            Console.Error.WriteLine($"Ошибка: файл промпта '{PromptFile}' не найден.");
            return null;
        }

        return await File.ReadAllTextAsync(path, Encoding.UTF8);
    }

    private static async Task<string?> LoadLocalFallbackAsync()
    {
        var fallbackPath = ResolveFallbackPath(LocalFallbackFile);
        if (fallbackPath is null)
        {
            Console.Error.WriteLine($"Ошибка: файл-резерв '{LocalFallbackFile}' не найден.");
            return null;
        }

        return await File.ReadAllTextAsync(fallbackPath, Encoding.UTF8);
    }

    private static async Task<string> LoadJsonAsync(string url)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string? ResolveFallbackPath(string fileName)
    {
        var current = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(current))
        {
            return current;
        }

        var baseDir = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(baseDir))
        {
            return baseDir;
        }

        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var projectPath = Path.Combine(projectDir, fileName);
        if (File.Exists(projectPath))
        {
            return projectPath;
        }

        return null;
    }

    private static string ResolveOutputPath(string fileName)
    {
        return Path.Combine(Environment.CurrentDirectory, fileName);
    }

    private static Encoding GetUtf8NoBom()
    {
        return new UTF8Encoding(false);
    }

    private static async Task UploadConfigAsync(string url, string jsonContent)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var fileBytes = Encoding.UTF8.GetBytes(jsonContent);

        var boundary = "----WebKitFormBoundary" + Guid.NewGuid().ToString("N");
        while (jsonContent.Contains(boundary, StringComparison.Ordinal))
            boundary = "----WebKitFormBoundary" + Guid.NewGuid().ToString("N");
        var headerBuilder = new StringBuilder();
        headerBuilder.Append("--").Append(boundary).Append("\r\n");
        headerBuilder.Append("Content-Disposition: form-data; name=\"f\"; filename=\"settings.bin\"\r\n");
        headerBuilder.Append("Content-Type: application/octet-stream\r\n\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(headerBuilder.ToString());
        var endBoundary = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");

        using var body = new MemoryStream();
        body.Write(headerBytes, 0, headerBytes.Length);
        body.Write(fileBytes, 0, fileBytes.Length);
        body.Write(endBoundary, 0, endBoundary.Length);
        body.Seek(0, SeekOrigin.Begin);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new StreamContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data");
        request.Content.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("boundary", boundary));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Console.Error.WriteLine("Файл успешно отправлен на устройство.");
    }

    private static void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"[{timestamp}] {message}{Environment.NewLine}";
        File.AppendAllText(ResolveOutputPath(LlmLogFile), line, Encoding.UTF8);
    }

    private static bool IsLikelyJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().TrimStart();
        if (span.Length == 0)
        {
            return false;
        }

        return span[0] == '{' || span[0] == '[';
    }

    private static bool IsConnectionReset(HttpRequestException ex)
    {
        if (ex.InnerException is IOException io && io.InnerException is SocketException socket)
        {
            return socket.SocketErrorCode == SocketError.ConnectionReset;
        }

        if (ex.InnerException is SocketException direct)
        {
            return direct.SocketErrorCode == SocketError.ConnectionReset;
        }

        return false;
    }

    // ===== Разбор ответа LLM =====
    private static bool TryExtractJson(string response, out string json, out string? comment, out string error)
    {
        const string CommentMarker = "<<<COMMENT>>>";
        const string JsonMarker = "<<<JSON>>>";
        const string EndMarker = "<<<END_JSON>>>";

        json = string.Empty;
        comment = null;
        error = string.Empty;

        var jsonIndex = response.IndexOf(JsonMarker, StringComparison.Ordinal);
        var endIndex = response.IndexOf(EndMarker, StringComparison.Ordinal);
        if (jsonIndex < 0 || endIndex < 0 || endIndex <= jsonIndex)
        {
            json = response.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "ответ пуст.";
                return false;
            }

            return true;
        }

        var commentIndex = response.IndexOf(CommentMarker, StringComparison.Ordinal);
        if (commentIndex >= 0 && commentIndex < jsonIndex)
        {
            comment = response[(commentIndex + CommentMarker.Length)..jsonIndex].Trim();
        }

        json = response[(jsonIndex + JsonMarker.Length)..endIndex].Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "JSON-блок пуст.";
            return false;
        }

        if (!(json.StartsWith('{') || json.StartsWith('[')))
        {
            json = response.Trim();
            if (!(json.StartsWith('{') || json.StartsWith('[')))
            {
                error = "извлечённый блок не является JSON.";
                return false;
            }
        }

        return true;
    }

    private static bool TryGetActions(JsonElement root, out List<JsonElement> actions, out string error)
    {
        actions = new List<JsonElement>();
        error = string.Empty;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    actions.Add(item);
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("actions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        actions.Add(item);
                    }
                }
            }
            else
            {
                actions.Add(root);
            }
        }

        if (actions.Count == 0)
        {
            error = "ответ не содержит actions.";
            return false;
        }

        return true;
    }

    // ===== Парсинг и индекс =====
    private static JsonObject? ParseRootObject(string json, out bool hadDuplicates)
    {
        hadDuplicates = false;
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!reader.Read())
        {
            return null;
        }

        var node = ParseNode(ref reader, ref hadDuplicates);
        return node as JsonObject;
    }

    private static JsonNode? ParseNode(ref Utf8JsonReader reader, ref bool hadDuplicates)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var obj = new JsonObject();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Ожидалось имя свойства.");
                    }

                    var name = reader.GetString() ?? string.Empty;
                    if (!reader.Read())
                    {
                        throw new JsonException("Ожидалось значение свойства.");
                    }

                    var value = ParseNode(ref reader, ref hadDuplicates);
                    if (obj.ContainsKey(name))
                    {
                        hadDuplicates = true;
                        obj[name] = value;
                    }
                    else
                    {
                        obj.Add(name, value);
                    }
                }

                return obj;
            }
            case JsonTokenType.StartArray:
            {
                var array = new JsonArray();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }

                    var item = ParseNode(ref reader, ref hadDuplicates);
                    array.Add(item);
                }

                return array;
            }
            case JsonTokenType.String:
                return JsonValue.Create(reader.GetString());
            case JsonTokenType.Number:
            {
                if (reader.TryGetInt32(out var intValue))
                {
                    return JsonValue.Create(intValue);
                }

                if (reader.TryGetInt64(out var longValue))
                {
                    return JsonValue.Create(longValue);
                }

                if (reader.TryGetDecimal(out var decimalValue))
                {
                    return JsonValue.Create(decimalValue);
                }

                return JsonValue.Create(reader.GetDouble());
            }
            case JsonTokenType.True:
            case JsonTokenType.False:
                return JsonValue.Create(reader.GetBoolean());
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Неподдерживаемый токен: {reader.TokenType}");
        }
    }

    private static IndexResult BuildElementIndex(JsonObject root)
    {
        var items = new List<ElementIndexItem>();
        var index = new Dictionary<string, ElementRef>(StringComparer.OrdinalIgnoreCase);
        var elementsList = new List<ElementRef>();

        var guiConf = root["gui_conf"] as JsonObject ?? root;
        if (guiConf["pages"] is JsonArray pages)
        {
            for (var p = 0; p < pages.Count; p++)
            {
                if (pages[p] is not JsonObject page)
                {
                    continue;
                }

                var pageLabel = page["lb"]?.GetValue<string>() ?? string.Empty;
                if (page["elements"] is not JsonArray elements)
                {
                    continue;
                }

                for (var e = 0; e < elements.Count; e++)
                {
                    if (elements[e] is not JsonObject element)
                    {
                        continue;
                    }

                    var id = $"p{p}.e{e}";
                    AddElement(index, items, elementsList, element, elements, id, pageLabel, false, null);

                    if (element["btns"] is JsonArray buttons)
                    {
                        for (var b = 0; b < buttons.Count; b++)
                        {
                            if (buttons[b] is not JsonObject btn)
                            {
                                continue;
                            }

                            var btnId = $"p{p}.e{e}.b{b}";
                            var parentLabel = element["lb"]?.GetValue<string>() ?? string.Empty;
                            AddElement(index, items, elementsList, btn, buttons, btnId, pageLabel, true, parentLabel);
                        }
                    }
                }
            }
        }

        AddButtonConfElements(root, items, index, elementsList);

        return new IndexResult(items, index, elementsList);
    }

    private static string BuildElementsPayload(IndexResult indexResult)
    {
        var elements = indexResult.Items.Select(item => new
        {
            id = item.Id,
            label = item.Label,
            kind = item.Kind,
            page = item.Page,
            x = item.X,
            y = item.Y,
            type = item.Type,
            nested = item.Nested,
            parent_label = item.ParentLabel,
            index = item.Index,
            display_index = item.DisplayIndex
        }).ToList();

        return JsonSerializer.Serialize(elements, AppJson.File);
    }

    private static void AddElement(
        Dictionary<string, ElementRef> index,
        List<ElementIndexItem> items,
        List<ElementRef> elementsList,
        JsonObject element,
        JsonArray siblings,
        string id,
        string pageLabel,
        bool isNested,
        string? parentLabel)
    {
        var label = element["lb"]?.GetValue<string>() ?? string.Empty;
        elementsList.Add(new ElementRef(element, siblings, pageLabel, label, isNested));
        index[id] = new ElementRef(element, siblings, pageLabel, label, isNested);

        int? x = TryGetInt(element, "x", out var xv) ? xv : null;
        int? y = TryGetInt(element, "y", out var yv) ? yv : null;
        int? type = TryGetInt(element, "type", out var tv) ? tv : null;

        items.Add(new ElementIndexItem(id, label, "gui", pageLabel, x, y, type, isNested, parentLabel, null, null));
    }

    private static void AddButtonConfElements(
        JsonObject root,
        List<ElementIndexItem> items,
        Dictionary<string, ElementRef> index,
        List<ElementRef> elementsList)
    {
        if (root["btn_conf"] is not JsonObject btnConf)
        {
            return;
        }

        if (btnConf["btns"] is not JsonArray btns)
        {
            return;
        }

        var total = btns.Count;
        for (var i = 0; i < total; i++)
        {
            if (btns[i] is not JsonObject btn)
            {
                continue;
            }

            var id = $"btn.b{i}";
            var label = btn["lb"]?.GetValue<string>() ?? string.Empty;
            var displayIndex = total - i;

            elementsList.Add(new ElementRef(btn, btns, "btn_conf", label, false));
            index[id] = new ElementRef(btn, btns, "btn_conf", label, false);

            items.Add(new ElementIndexItem(id, label, "btn_conf", "btn_conf", null, null, null, false, null, i + 1, displayIndex));
        }
    }

    // ===== Применение действий =====
    private static bool TryApplyLlmAction(
        JsonElement actionRoot,
        IndexResult indexResult,
        IReadOnlyList<int> buttonDisplayIndices,
        int? forcedColor,
        ColorAdjustment? forcedAdjustment,
        out string error,
        out string info)
    {
        error = string.Empty;
        info = string.Empty;

        var action = GetString(actionRoot, "action")?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
        {
            error = "ответ не содержит поля action.";
            return false;
        }

        action = NormalizeAction(action);

        if (forcedColor.HasValue)
        {
            action = "set_color";
        }
        else if (forcedAdjustment.HasValue)
        {
            action = "adjust_color";
        }

        var targetId = GetString(actionRoot, "target_id")
                       ?? GetString(actionRoot, "targetId")
                       ?? GetString(actionRoot, "target")
                       ?? GetString(actionRoot, "id");

        ElementRef? target = null;
        if (!string.IsNullOrWhiteSpace(targetId) && indexResult.Index.TryGetValue(targetId, out var byId))
        {
            target = byId;
        }
        else
        {
            var targetLabel = GetString(actionRoot, "target_label")
                              ?? GetString(actionRoot, "targetLabel")
                              ?? GetString(actionRoot, "label");
            if (!string.IsNullOrWhiteSpace(targetLabel))
            {
                target = FindTargetByLabel(indexResult.Elements, targetLabel);
            }
        }

        if (target is null)
        {
            var displayIndex = GetInt(actionRoot, "target_display_index")
                               ?? GetInt(actionRoot, "display_index")
                               ?? GetInt(actionRoot, "displayIndex");
            if (displayIndex.HasValue)
            {
                target = FindTargetByDisplayIndex(indexResult.Items, indexResult.Index, displayIndex.Value);
            }
        }

        if (target is null)
        {
            var index = GetInt(actionRoot, "target_index")
                        ?? GetInt(actionRoot, "index")
                        ?? GetInt(actionRoot, "targetIndex");
            if (index.HasValue)
            {
                target = FindTargetByIndex(indexResult.Items, indexResult.Index, index.Value);
            }
        }

        if (buttonDisplayIndices.Count > 0)
        {
            var overrideTarget = FindTargetByDisplayIndex(indexResult.Items, indexResult.Index, buttonDisplayIndices[0]);
            if (overrideTarget is not null && (target is null || !ReferenceEquals(target.Value.Element, overrideTarget.Value.Element)))
            {
                target = overrideTarget;
                AppendLog($"Target override by displayIndex {buttonDisplayIndices[0]}");
            }
        }

        if (target is null)
        {
            error = "не удалось определить target_id.";
            return false;
        }

        var parameters = actionRoot.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object
            ? paramsElement
            : default;

        if (action == "move" && string.Equals(target.Value.PageLabel, "btn_conf", StringComparison.OrdinalIgnoreCase))
        {
            error = "для btn_conf нельзя использовать move.";
            return false;
        }

        ElementRef? secondOverride = null;
        if (action == "swap" && buttonDisplayIndices.Count >= 2)
        {
            var firstOverride = FindTargetByDisplayIndex(indexResult.Items, indexResult.Index, buttonDisplayIndices[0]);
            var secondCandidate = FindTargetByDisplayIndex(indexResult.Items, indexResult.Index, buttonDisplayIndices[1]);
            if (secondCandidate is not null)
            {
                if (firstOverride is null || !ReferenceEquals(firstOverride.Value.Element, secondCandidate.Value.Element))
                {
                    secondOverride = secondCandidate;
                    AppendLog($"Swap override by displayIndex {buttonDisplayIndices[0]} and {buttonDisplayIndices[1]}");
                }
                else
                {
                    AppendLog($"Swap override resolved to same element for indices {buttonDisplayIndices[0]} and {buttonDisplayIndices[1]}");
                }
            }
        }

        return action switch
        {
            "move" => ApplyMoveAction(target.Value, parameters, out error, out info),
            "rename" => ApplyRenameAction(target.Value, parameters, out error, out info),
            "set_color" => ApplySetColorAction(target.Value, parameters, forcedColor, out error, out info),
            "adjust_color" => ApplyAdjustColorAction(target.Value, parameters, forcedAdjustment, out error, out info),
            "swap" => ApplySwapAction(target.Value, secondOverride, actionRoot, indexResult, out error, out info),
            _ => FailUnknownAction(action, out error, out info)
        };
    }

    private static bool FailUnknownAction(string action, out string error, out string info)
    {
        info = string.Empty;
        error = $"неподдерживаемое действие: {action}.";
        return false;
    }

    private static string NormalizeAction(string action)
    {
        return action switch
        {
            "color" => "set_color",
            "setcolor" => "set_color",
            "set-color" => "set_color",
            "adjust" => "adjust_color",
            "adjustcolor" => "adjust_color",
            "adjust-color" => "adjust_color",
            _ => action
        };
    }

    // ===== Действия: move/rename/color/swap =====
    private static bool ApplyMoveAction(ElementRef target, JsonElement parameters, out string error, out string info)
    {
        error = string.Empty;
        info = string.Empty;

        if (!TryGetInt(target.Element, "x", out var x) || !TryGetInt(target.Element, "y", out var y))
        {
            error = "у элемента нет координат x/y для перемещения.";
            return false;
        }

        var column = GetInt(parameters, "column") ?? GetInt(parameters, "col");
        var row = GetInt(parameters, "row");
        if (column.HasValue || row.HasValue)
        {
            if (column.HasValue)
            {
                if (column < 1 || column > GridXColumns)
                {
                    error = $"номер столбца вне диапазона 1..{GridXColumns}.";
                    return false;
                }

                target.Element["x"] = GridXBase + (column.Value - 1) * GridXStep;
            }

            if (row.HasValue)
            {
                if (row < 1 || row > GridYRows)
                {
                    error = $"номер строки вне диапазона 1..{GridYRows}.";
                    return false;
                }

                target.Element["y"] = GridYBase + (row.Value - 1) * GridYStep;
            }

            info = $"Перемещение '{target.Label}' к ячейке ({column ?? 0}, {row ?? 0}).";
            return true;
        }

        var direction = GetString(parameters, "direction")?.ToLowerInvariant();
        var steps = GetInt(parameters, "steps") ?? 1;
        if (string.IsNullOrWhiteSpace(direction))
        {
            error = "для перемещения требуется direction или column/row.";
            return false;
        }

        var delta = (direction is "left" or "right" ? GridXStep : GridYStep) * steps;
        switch (direction)
        {
            case "right":
                target.Element["x"] = x + delta;
                info = $"Смещение '{target.Label}' вправо на {steps} шаг(а).";
                return true;
            case "left":
                target.Element["x"] = x - delta;
                info = $"Смещение '{target.Label}' влево на {steps} шаг(а).";
                return true;
            case "down":
                target.Element["y"] = y + delta;
                info = $"Смещение '{target.Label}' вниз на {steps} шаг(а).";
                return true;
            case "up":
                target.Element["y"] = y - delta;
                info = $"Смещение '{target.Label}' вверх на {steps} шаг(а).";
                return true;
            default:
                error = $"неизвестное направление: {direction}.";
                return false;
        }
    }

    private static bool ApplyRenameAction(ElementRef target, JsonElement parameters, out string error, out string info)
    {
        error = string.Empty;
        info = string.Empty;

        var newLabel = GetString(parameters, "new_label") ?? GetString(parameters, "label") ?? GetString(parameters, "name");
        if (string.IsNullOrWhiteSpace(newLabel))
        {
            error = "не указано новое имя (params.new_label).";
            return false;
        }

        if (!target.Element.ContainsKey("lb"))
        {
            error = "у элемента нет поля lb для переименования.";
            return false;
        }

        target.Element["lb"] = newLabel.Trim();
        info = $"Переименование '{target.Label}' -> '{newLabel.Trim()}'.";
        return true;
    }

    private static bool ApplySetColorAction(ElementRef target, JsonElement parameters, int? forcedColor, out string error, out string info)
    {
        error = string.Empty;
        info = string.Empty;

        var color = 0;
        if (forcedColor.HasValue)
        {
            color = forcedColor.Value;
        }
        else if (!TryGetColor(parameters, out color))
        {
            error = "не удалось определить цвет (params.color).";
            return false;
        }

        var scope = ResolveScopeFromParams(parameters, defaultAll: true);
        var updated = 0;
        var applyText = scope.HasFlag(ColorScope.Text);
        var applyBorderBackground = scope.HasFlag(ColorScope.Border) || scope.HasFlag(ColorScope.Background);
        var isButton = HasButtonColorFields(target.Element);

        if (applyText)
        {
            updated += UpdateColorIfExists(target.Element, "c_text_off", color);
        }

        if (applyBorderBackground)
        {
            updated += UpdateBorderAndBackground(target.Element, color);
        }

        if (isButton)
        {
            updated += UpdateButtonColors(target.Element, color);
        }

        if (updated == 0)
        {
            error = "подходящие поля цвета не найдены в объекте.";
            return false;
        }

        EnsureBackgroundOnMatchesBorder(target.Element);
        info = $"Изменение цвета '{target.Label}' на {color}.";
        return true;
    }

    private static bool ApplyAdjustColorAction(ElementRef target, JsonElement parameters, ColorAdjustment? forcedAdjustment, out string error, out string info)
    {
        error = string.Empty;
        info = string.Empty;

        var adjustment = forcedAdjustment ?? ResolveAdjustment(parameters);
        if (adjustment == ColorAdjustment.None)
        {
            error = "не удалось определить тип коррекции (params.adjust).";
            return false;
        }

        if (!TryGetBaseColor(target.Element, out var baseColor))
        {
            error = "не удалось определить текущий цвет элемента.";
            return false;
        }

        var adjustedBase = ApplyAdjustment(baseColor, adjustment);
        var scope = ResolveScopeFromParams(parameters, defaultAll: false);
        var updated = 0;
        var applyText = scope.HasFlag(ColorScope.Text);
        var applyBorderBackground = scope.HasFlag(ColorScope.Border) || scope.HasFlag(ColorScope.Background);
        var isButton = HasButtonColorFields(target.Element);

        if (applyText)
        {
            updated += UpdateColorIfExists(target.Element, "c_text_off", adjustedBase);
        }

        if (applyBorderBackground)
        {
            updated += UpdateBorderAndBackground(target.Element, adjustedBase);
        }

        if (isButton)
        {
            updated += UpdateButtonColors(target.Element, adjustedBase);
        }

        if (updated == 0)
        {
            error = "подходящие поля цвета не найдены в объекте.";
            return false;
        }

        EnsureBackgroundOnMatchesBorder(target.Element);
        info = $"Коррекция цвета '{target.Label}' ({adjustment}).";
        return true;
    }

    private static bool ApplySwapAction(ElementRef first, ElementRef? secondOverride, JsonElement actionRoot, IndexResult indexResult, out string error, out string info)
    {
        error = string.Empty;
        info = string.Empty;

        ElementRef? second = secondOverride;

        var targetId1 = GetString(actionRoot, "target_id")
                        ?? GetString(actionRoot, "targetId")
                        ?? GetString(actionRoot, "target")
                        ?? GetString(actionRoot, "id");
        var targetId2 = GetString(actionRoot, "target_id_2")
                       ?? GetString(actionRoot, "targetId2")
                       ?? GetString(actionRoot, "target_id2")
                       ?? GetString(actionRoot, "target2")
                       ?? GetString(actionRoot, "id2")
                       ?? GetString(actionRoot, "second_id");

        if (second is null && !string.IsNullOrWhiteSpace(targetId2) && indexResult.Index.TryGetValue(targetId2, out var byId))
        {
            second = byId;
        }
        else
        {
            var targetLabel2 = GetString(actionRoot, "target_label_2")
                               ?? GetString(actionRoot, "targetLabel2")
                               ?? GetString(actionRoot, "label2")
                               ?? GetString(actionRoot, "second_label");
            if (second is null && !string.IsNullOrWhiteSpace(targetLabel2))
            {
                second = FindTargetByLabel(indexResult.Elements, targetLabel2);
            }
        }

        if (second is null)
        {
            error = "не удалось определить второй target_id для swap.";
            return false;
        }

        if (ReferenceEquals(first.Element, second.Value.Element))
        {
            if (string.Equals(first.PageLabel, "btn_conf", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseBtnConfId(targetId1, out var idx1ById) &&
                    TryParseBtnConfId(targetId2, out var idx2ById) &&
                    idx1ById != idx2ById &&
                    idx1ById >= 0 && idx2ById >= 0 &&
                    idx1ById < first.Siblings.Count && idx2ById < first.Siblings.Count)
                {
                    return SwapByIndex(first.Siblings, idx1ById, idx2ById, out error, out info);
                }
            }

            error = "попытка поменять один и тот же элемент.";
            return false;
        }

        var x1 = 0;
        var y1 = 0;
        var x2 = 0;
        var y2 = 0;
        var label1 = GetLabel(first.Element, first.Label);
        var label2 = GetLabel(second.Value.Element, second.Value.Label);
        var firstHasCoords = TryGetInt(first.Element, "x", out x1) && TryGetInt(first.Element, "y", out y1);
        var secondHasCoords = TryGetInt(second.Value.Element, "x", out x2) && TryGetInt(second.Value.Element, "y", out y2);

        if (firstHasCoords && secondHasCoords)
        {
            first.Element["x"] = x2;
            first.Element["y"] = y2;
            second.Value.Element["x"] = x1;
            second.Value.Element["y"] = y1;
            info = $"Поменяны местами '{label1}' и '{label2}' (координаты).";
            return true;
        }

        if (ReferenceEquals(first.Siblings, second.Value.Siblings))
        {
            var idx1 = FindIndex(first.Siblings, first.Element);
            var idx2 = FindIndex(second.Value.Siblings, second.Value.Element);
            if (idx1 < 0 || idx2 < 0)
            {
                error = "не удалось найти элементы для swap в массиве.";
                return false;
            }
            if (idx1 == idx2)
            {
                error = "попытка поменять один и тот же элемент.";
                return false;
            }

            var isBtnConf = string.Equals(first.PageLabel, "btn_conf", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(second.Value.PageLabel, "btn_conf", StringComparison.OrdinalIgnoreCase);
            var displayIndex1 = isBtnConf ? GetDisplayIndex(first.Siblings.Count, idx1) : 0;
            var displayIndex2 = isBtnConf ? GetDisplayIndex(first.Siblings.Count, idx2) : 0;
            var identical = JsonNode.DeepEquals(first.Siblings[idx1], first.Siblings[idx2]);

            var clone1 = first.Siblings[idx1]?.DeepClone();
            var clone2 = first.Siblings[idx2]?.DeepClone();
            if (clone1 is null || clone2 is null)
            {
                error = "не удалось клонировать элементы для swap.";
                return false;
            }

            first.Siblings[idx1] = clone2;
            first.Siblings[idx2] = clone1;
            var identityNote = identical ? " (элементы одинаковы, JSON может не измениться)" : string.Empty;
            var dispInfo = isBtnConf ? $", disp {displayIndex1}<->{displayIndex2}" : string.Empty;
            info = $"Поменяны местами '{label1}' и '{label2}' (порядок: idx {idx1}<->{idx2}{dispInfo}){identityNote}.";
            return true;
        }

        error = "swap возможен только для элементов с координатами или в одном списке.";
        return false;
    }

    // ===== Парсинг параметров =====
    private static ColorScope ResolveScopeFromParams(JsonElement parameters, bool defaultAll)
    {
        var scopeText = GetString(parameters, "scope")?.ToLowerInvariant();
        return scopeText switch
        {
            "text" => ColorScope.Text,
            "background" => ColorScope.Background,
            "border" => ColorScope.Border,
            "all" => ColorScope.Text | ColorScope.Background | ColorScope.Border,
            _ => defaultAll ? ColorScope.Text | ColorScope.Background | ColorScope.Border : ColorScope.Background
        };
    }

    private static ColorAdjustment ResolveAdjustment(JsonElement parameters)
    {
        var adjust = GetString(parameters, "adjust")?.ToLowerInvariant();
        return adjust switch
        {
            "darker" => ColorAdjustment.Darken,
            "darken" => ColorAdjustment.Darken,
            "lighter" => ColorAdjustment.Lighten,
            "lighten" => ColorAdjustment.Lighten,
            "redder" => ColorAdjustment.MoreRed,
            "bluer" => ColorAdjustment.MoreBlue,
            "greener" => ColorAdjustment.MoreGreen,
            "yellower" => ColorAdjustment.MoreYellow,
            _ => ColorAdjustment.None
        };
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

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (int.TryParse(text, out intValue))
            {
                return intValue;
            }
        }

        return null;
    }

    private static bool TryGetColor(JsonElement parameters, out int color)
    {
        color = 0;
        var colorText = GetString(parameters, "color") ?? GetString(parameters, "color_value") ?? GetString(parameters, "value");
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return false;
        }

        return TryParseColor(colorText, out color);
    }

    private static bool TryParseColor(string text, out int color)
    {
        var trimmed = text.Trim();
        if (Regex.IsMatch(trimmed, @"^#([0-9a-fA-F]{6})$") &&
            int.TryParse(trimmed[1..], System.Globalization.NumberStyles.HexNumber, null, out color))
        {
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^0x([0-9a-fA-F]{6})$", RegexOptions.IgnoreCase) &&
            int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out color))
        {
            return true;
        }

        if (int.TryParse(trimmed, out color))
        {
            return true;
        }

        var named = ResolveNamedColor(Normalize(trimmed));
        if (named.HasValue)
        {
            color = named.Value;
            return true;
        }

        return false;
    }

    private static ElementRef? FindTargetByLabel(List<ElementRef> elements, string label)
    {
        var normalizedLabel = Normalize(label);
        if (string.IsNullOrWhiteSpace(normalizedLabel))
        {
            return null;
        }

        foreach (var element in elements)
        {
            if (Normalize(element.Label) == normalizedLabel)
            {
                return element;
            }
        }

        return elements
            .OrderByDescending(item => Normalize(item.Label).Length)
            .FirstOrDefault(item => Normalize(item.Label).Contains(normalizedLabel, StringComparison.Ordinal));
    }

    private static ElementRef? FindTargetByDisplayIndex(List<ElementIndexItem> items, Dictionary<string, ElementRef> index, int displayIndex)
    {
        var match = items.FirstOrDefault(item => item.Kind == "btn_conf" && item.DisplayIndex == displayIndex);
        if (!string.IsNullOrWhiteSpace(match.Id) && index.TryGetValue(match.Id, out var element))
        {
            return element;
        }

        return null;
    }

    private static ElementRef? FindTargetByIndex(List<ElementIndexItem> items, Dictionary<string, ElementRef> index, int indexValue)
    {
        var match = items.FirstOrDefault(item => item.Kind == "btn_conf" && item.Index == indexValue);
        if (!string.IsNullOrWhiteSpace(match.Id) && index.TryGetValue(match.Id, out var element))
        {
            return element;
        }

        return null;
    }

    private static string GetLabel(JsonObject element, string fallback)
    {
        if (element.TryGetPropertyValue("lb", out var node) && node is not null)
        {
            var label = node.ToString();
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
        }

        return fallback;
    }

    private static int GetDisplayIndex(int totalCount, int index)
    {
        if (totalCount <= 0 || index < 0)
        {
            return 0;
        }

        return totalCount - index;
    }

    private static bool TryParseBtnConfId(string? id, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var match = Regex.Match(id, @"^btn\.b(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var parsed))
        {
            return false;
        }

        index = parsed;
        return true;
    }

    private static bool SwapByIndex(JsonArray siblings, int idx1, int idx2, out string error, out string info)
    {
        error = string.Empty;
        info = string.Empty;

        if (idx1 < 0 || idx2 < 0 || idx1 >= siblings.Count || idx2 >= siblings.Count || idx1 == idx2)
        {
            error = "некорректные индексы для swap.";
            return false;
        }

        var node1 = siblings[idx1];
        var node2 = siblings[idx2];
        var label1 = node1 is JsonObject obj1 ? GetLabel(obj1, $"idx{idx1}") : $"idx{idx1}";
        var label2 = node2 is JsonObject obj2 ? GetLabel(obj2, $"idx{idx2}") : $"idx{idx2}";
        var identical = JsonNode.DeepEquals(node1, node2);

        var clone1 = node1?.DeepClone();
        var clone2 = node2?.DeepClone();
        if (clone1 is null || clone2 is null)
        {
            error = "не удалось клонировать элементы для swap.";
            return false;
        }

        siblings[idx1] = clone2;
        siblings[idx2] = clone1;
        var dispInfo = $", disp {GetDisplayIndex(siblings.Count, idx1)}<->{GetDisplayIndex(siblings.Count, idx2)}";
        var identityNote = identical ? " (элементы одинаковы, JSON может не измениться)" : string.Empty;
        info = $"Поменяны местами '{label1}' и '{label2}' (порядок: idx {idx1}<->{idx2}{dispInfo}){identityNote}.";
        return true;
    }

    private static List<int> ExtractButtonDisplayIndices(string instruction)
    {
        var results = new List<int>();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return results;
        }

        foreach (Match match in Regex.Matches(instruction, @"(?:кнопк\w*|button\w*)\s*№?\s*(\d+)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, out var value))
            {
                results.Add(value);
            }
        }

        foreach (Match match in Regex.Matches(instruction, @"(\d+)\s*(?:кнопк\w*|button\w*)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, out var value))
            {
                results.Add(value);
            }
        }

        return results.Distinct().ToList();
    }

    private static int? ResolveNamedColorFromInstruction(string instruction)
    {
        var normalized = Normalize(instruction);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ContainsWord(normalized, "красн") || normalized.Contains("red"))
        {
            return 0xFF0000;
        }

        if (ContainsWord(normalized, "оранж") || ContainsWord(normalized, "апельс") || normalized.Contains("orange"))
        {
            return 0xFFA500;
        }

        if (ContainsWord(normalized, "желт") || normalized.Contains("yellow"))
        {
            return 0xFFFF00;
        }

        if (ContainsWord(normalized, "зелен") || normalized.Contains("green"))
        {
            return 0x00FF00;
        }

        if (ContainsWord(normalized, "син") || normalized.Contains("blue"))
        {
            return 0x0000FF;
        }

        if (ContainsWord(normalized, "фиолет") || normalized.Contains("purple") || normalized.Contains("violet"))
        {
            return 0x800080;
        }

        if (ContainsWord(normalized, "розов") || normalized.Contains("pink"))
        {
            return 0xFFC0CB;
        }

        if (ContainsWord(normalized, "бел") || normalized.Contains("white"))
        {
            return 0xFFFFFF;
        }

        if (ContainsWord(normalized, "черн") || normalized.Contains("black"))
        {
            return 0x000000;
        }

        if (ContainsWord(normalized, "сер") || normalized.Contains("gray") || normalized.Contains("grey"))
        {
            return 0x808080;
        }

        return null;
    }

    private static ColorAdjustment? ResolveAdjustmentFromInstruction(string instruction)
    {
        var normalized = Normalize(instruction);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ContainsWord(normalized, "потемн") || ContainsWord(normalized, "темнее") || ContainsWord(normalized, "темней") || ContainsWord(normalized, "приглуш"))
        {
            return ColorAdjustment.Darken;
        }

        if (ContainsWord(normalized, "поярч") || ContainsWord(normalized, "ярче") || ContainsWord(normalized, "светле"))
        {
            return ColorAdjustment.Lighten;
        }

        return null;
    }

    private static bool ContainsWord(string text, string fragment)
    {
        return text.Contains(fragment, StringComparison.Ordinal);
    }

    private static int? ResolveNamedColor(string normalizedInstruction)
    {
        return normalizedInstruction switch
        {
            var text when text.Contains("красн") => 0xFF0000,
            var text when text.Contains("зелен") => 0x00FF00,
            var text when text.Contains("син") => 0x0000FF,
            var text when text.Contains("бел") => 0xFFFFFF,
            var text when text.Contains("черн") => 0x000000,
            var text when text.Contains("желт") => 0xFFFF00,
            var text when text.Contains("оранж") => 0xFFA500,
            var text when text.Contains("фиолет") => 0x800080,
            var text when text.Contains("розов") => 0xFFC0CB,
            var text when text.Contains("сер") => 0x808080,
            _ => null
        };
    }

    // ===== Цвета и арифметика =====
    private static int UpdateColorIfExists(JsonObject element, string key, int color)
    {
        if (element.ContainsKey(key))
        {
            element[key] = color;
            return 1;
        }

        return 0;
    }

    private static int UpdateBorderAndBackground(JsonObject element, int baseColor)
    {
        var updated = 0;
        var backgroundColor = DarkenColor(baseColor, 0.1);
        updated += UpdateColorIfExists(element, "c_border_off", baseColor);
        updated += UpdateColorIfExists(element, "c_border_on", baseColor);
        updated += UpdateColorIfExists(element, "c_bg_on", baseColor);
        updated += UpdateColorIfExists(element, "c_bg_off", backgroundColor);
        return updated;
    }

    private static int UpdateButtonColors(JsonObject element, int baseColor)
    {
        var updated = 0;
        var backgroundColor = DarkenColor(baseColor, 0.1);
        updated += UpdateColorIfExists(element, "c_btn_on", baseColor);
        updated += UpdateColorIfExists(element, "c_btn_off", backgroundColor);
        return updated;
    }

    private static bool HasButtonColorFields(JsonObject element)
    {
        return element.ContainsKey("c_btn_on") || element.ContainsKey("c_btn_off");
    }

    private static void EnsureBackgroundOnMatchesBorder(JsonObject element)
    {
        if (TryGetInt(element, "c_border_on", out var borderOn) && element.ContainsKey("c_bg_on"))
        {
            element["c_bg_on"] = borderOn;
        }
    }

    private static bool TryGetBaseColor(JsonObject element, out int color)
    {
        return TryGetInt(element, "c_border_on", out color)
               || TryGetInt(element, "c_border_off", out color)
               || TryGetInt(element, "c_bg_on", out color)
               || TryGetInt(element, "c_bg_off", out color)
               || TryGetInt(element, "c_text_off", out color)
               || TryGetInt(element, "c_btn_on", out color)
               || TryGetInt(element, "c_btn_off", out color);
    }

    private static int FindIndex(JsonArray array, JsonObject element)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (ReferenceEquals(array[i], element))
            {
                return i;
            }

            if (array[i] is JsonObject obj && ReferenceEquals(obj, element))
            {
                return i;
            }
        }

        return -1;
    }

    private static int ApplyAdjustment(int color, ColorAdjustment adjustment)
    {
        return adjustment switch
        {
            ColorAdjustment.Darken => AdjustBrightness(color, 0.85),
            ColorAdjustment.Lighten => AdjustBrightness(color, 1.15),
            ColorAdjustment.MoreRed => MixWith(color, 0xFF0000, 0.2),
            ColorAdjustment.MoreGreen => MixWith(color, 0x00FF00, 0.2),
            ColorAdjustment.MoreBlue => MixWith(color, 0x0000FF, 0.2),
            ColorAdjustment.MoreYellow => MixWith(color, 0xFFFF00, 0.2),
            _ => color
        };
    }

    private static int AdjustBrightness(int color, double factor)
    {
        var r = (color >> 16) & 0xFF;
        var g = (color >> 8) & 0xFF;
        var b = color & 0xFF;

        r = ClampToByte((int)Math.Round(r * factor));
        g = ClampToByte((int)Math.Round(g * factor));
        b = ClampToByte((int)Math.Round(b * factor));

        return (r << 16) | (g << 8) | b;
    }

    private static int MixWith(int color, int targetColor, double weight)
    {
        var r = (color >> 16) & 0xFF;
        var g = (color >> 8) & 0xFF;
        var b = color & 0xFF;

        var tr = (targetColor >> 16) & 0xFF;
        var tg = (targetColor >> 8) & 0xFF;
        var tb = targetColor & 0xFF;

        var nr = ClampToByte((int)Math.Round(r * (1 - weight) + tr * weight));
        var ng = ClampToByte((int)Math.Round(g * (1 - weight) + tg * weight));
        var nb = ClampToByte((int)Math.Round(b * (1 - weight) + tb * weight));

        return (nr << 16) | (ng << 8) | nb;
    }

    private static int DarkenColor(int color, double factor)
    {
        var r = (color >> 16) & 0xFF;
        var g = (color >> 8) & 0xFF;
        var b = color & 0xFF;

        r = ClampToByte((int)Math.Round(r * factor));
        g = ClampToByte((int)Math.Round(g * factor));
        b = ClampToByte((int)Math.Round(b * factor));

        return (r << 16) | (g << 8) | b;
    }

    private static int ClampToByte(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return value;
    }

    private static bool TryGetInt(JsonObject element, string key, out int value)
    {
        value = 0;
        if (!element.ContainsKey(key))
        {
            return false;
        }

        var node = element[key];
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out value))
            {
                return true;
            }
        }

        return int.TryParse(node.ToString(), out value);
    }

    private static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant();
        var cleaned = Regex.Replace(lower, @"[^a-z0-9а-яё\s]", " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
