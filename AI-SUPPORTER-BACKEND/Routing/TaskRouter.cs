using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class TaskRouter
{
    private readonly ToolRegistry _registry;
    private readonly ChatAssistant _chat;
    private readonly LlmRouter _router;
    private readonly IpRegistryService _ipRegistry;
    private string? _schemaText;
    private DateTime _lastRefresh;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private readonly ILogger<TaskRouter>? _logger;

    public TaskRouter(ToolRegistry registry, LlmClient llm, ChatAssistant chat, LlmRouter llmRouter, IpRegistryService ipRegistry, ILogger<TaskRouter>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ = llm ?? throw new ArgumentNullException(nameof(llm));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _router = llmRouter ?? throw new ArgumentNullException(nameof(llmRouter));
        _ipRegistry = ipRegistry ?? throw new ArgumentNullException(nameof(ipRegistry));
        _logger = logger;
    }

    public async Task RouteAsync(string userInput)
    {
        _logger?.LogInformation("[ROUTER] RouteAsync: {Input}", userInput);
        var result = await ProcessAsync(userInput);
        if (result is not null)
            _logger?.LogInformation("[ROUTER] Result: {Result}", result);
    }

    public async Task<string?> RouteWithResponseAsync(string userInput, IReadOnlyList<ChatMessage>? history = null, string? username = null, LlmTask chatTask = LlmTask.Chat)
    {
        _logger?.LogInformation("[ROUTER] RouteWithResponse: {Input}", userInput);
        var result = await ProcessAsync(userInput, history, username, chatTask);
        _logger?.LogInformation("[ROUTER] Result: {Result}", result ?? "(null)");
        return result;
    }

    public async Task<string?> RouteWithStreamingAsync(string userInput, IReadOnlyList<ChatMessage>? history = null, Func<string, Task>? onChunk = null, string? username = null, LlmTask chatTask = LlmTask.Chat)
    {
        _logger?.LogInformation("[ROUTER] RouteWithStreaming: {Input}", userInput);

        if (string.IsNullOrWhiteSpace(userInput))
        {
            _logger?.LogError("[ROUTER] Empty input");
            return null;
        }

        EnsureSchema();

        var user = username ?? RequestContext.Current.Value ?? "";
        var (toolClient, toolOpts, toolModel) = await _router.ResolveAsync(LlmTask.ToolCalling, user);
        var prompt = BuildPrompt(userInput);
        _logger?.LogInformation("[ROUTER] tool-проба: provider={Provider} model={Model}", toolOpts.UseApi == true ? "api" : "local", toolModel);
        var response = await toolClient.GenerateAsync(prompt, toolOpts);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger?.LogError("[ROUTER] Empty LLM response");
            _logger?.LogWarning("[ROUTER] tool-проба не удалась — фолбэк на чат-модель");
            return await FallbackToChatAsync(userInput, history, user, chatTask, onChunk);
        }

        if (!TaskCallParser.TryParse(response, out var call, out var parseError))
        {
            _logger?.LogError("[ROUTER] Parse error: {ParseError}", parseError);
            return "Не удалось обработать запрос.";
        }

        _logger?.LogInformation("[ROUTER] Parsed call: function={Function} args={ArgCount}", call.Function, call.Args?.Count ?? 0);

        if (call.IsClarify || call.IsNone)
        {
            _logger?.LogInformation("[ROUTER] Clarify/None (streaming): {Message}", call.Message);
            return await FallbackToChatAsync(userInput, history, user, chatTask, onChunk);
        }

        _logger?.LogInformation("[ROUTER] Calling tool: {Function} with {ArgCount} args", call.Function, call.Args?.Count ?? 0);
        var toolArgs = call.Args ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var toolResult = await _registry.ExecuteAsync(call.Function, toolArgs, userInput, RequestContext.Current.Value);
        var text = toolResult?.ToString();

        _logger?.LogInformation("[ROUTER] Tool result: {Result}", Log.Truncate(text));
        return text;
    }

    private async Task<string?> ProcessAsync(string userInput, IReadOnlyList<ChatMessage>? history = null, string? username = null, LlmTask chatTask = LlmTask.Chat)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            _logger?.LogError("[ROUTER] Empty input");
            return null;
        }

        EnsureSchema();

        _logger?.LogInformation("[ROUTER] Process: {Input}", userInput);

        var user = username ?? RequestContext.Current.Value ?? "";
        var (toolClient, toolOpts, toolModel) = await _router.ResolveAsync(LlmTask.ToolCalling, user);
        var prompt = BuildPrompt(userInput);
        _logger?.LogInformation("[ROUTER] tool-проба: provider={Provider} model={Model}", toolOpts.UseApi == true ? "api" : "local", toolModel);
        var response = await toolClient.GenerateAsync(prompt, toolOpts);

        var reqId = RequestContext.Current.Value;
        if (Log.DebugEnabled)
        {
            var prefix = reqId != null ? $"[REQ:{reqId}] " : "";
            Log.Debug($"{prefix}[LLM] raw: {Log.Truncate(response, 400)}");
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger?.LogError("[ROUTER] Empty LLM response");
            _logger?.LogWarning("[ROUTER] tool-проба не удалась — фолбэк на чат-модель");
            return await FallbackToChatAsync(userInput, history, user, chatTask, null);
        }

        if (!TaskCallParser.TryParse(response, out var call, out var parseError))
        {
            _logger?.LogError("[ROUTER] Parse error: {ParseError}", parseError);
            return "Не удалось обработать запрос.";
        }

        _logger?.LogInformation("[ROUTER] Parsed call: function={Function} args={ArgCount}", call.Function, call.Args?.Count ?? 0);

        if (call.IsClarify || call.IsNone)
        {
            _logger?.LogInformation("[ROUTER] Clarify/None: {Message}", call.Message);
            return await FallbackToChatAsync(userInput, history, user, chatTask, null);
        }

        var tool = _registry.Find(call.Function);
        if (tool is null)
        {
            _logger?.LogError("[ROUTER] Tool not found: {Function}", call.Function);
            return $"Инструмент '{call.Function}' не найден.";
        }

        _logger?.LogInformation("[ROUTER] Calling tool: {Function} with {ArgCount} args", call.Function, call.Args?.Count ?? 0);
        var toolArgs = call.Args ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var result = await _registry.ExecuteAsync(call.Function, toolArgs, userInput, RequestContext.Current.Value);
        var text = result?.ToString();

        _logger?.LogInformation("[ROUTER] Tool result: {Result}", Log.Truncate(text));
        return text;
    }

    private async Task<string?> FallbackToChatAsync(string userInput, IReadOnlyList<ChatMessage>? history, string user, LlmTask chatTask, Func<string, Task>? onChunk)
    {
        _logger?.LogInformation("[ROUTER] Fallback to chat: task={Task}", chatTask);
        var (_, chatOptions, _) = await _router.ResolveAsync(chatTask, user);
        if (onChunk is not null)
        {
            var sb = new StringBuilder();
            await foreach (var chunk in _chat.AskStreamingAsync(userInput, history, chatOptions))
            {
                sb.Append(chunk);
                await onChunk(chunk);
            }
            var answer = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(answer)) answer = "Не удалось получить ответ.";
            _logger?.LogInformation("[CHAT] {Answer}", Log.Truncate(answer));
            return answer;
        }

        var plainAnswer = await _chat.AskAsync(userInput, history, chatOptions);
        _logger?.LogInformation("[CHAT] {Answer}", Log.Truncate(plainAnswer));
        return plainAnswer;
    }

    private void EnsureSchema()
    {
        if (_schemaText is not null && DateTime.UtcNow - _lastRefresh < RefreshInterval)
            return;
        _schemaText = _registry.BuildToolsJson();
        _lastRefresh = DateTime.UtcNow;
    }

    private string BuildPrompt(string userInput)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ты — маршрутизатор функций. Выбери ровно ОДНУ функцию из списка и заполни аргументы.");
        sb.AppendLine("Верни ТОЛЬКО валидный JSON без пояснений и без Markdown.");
        sb.AppendLine();
        sb.AppendLine("Если нужно уточнение, верни:");
        sb.AppendLine("{\"function\":\"clarify\",\"message\":\"...\"}");
        sb.AppendLine();
        sb.AppendLine("Если ни одна функция не подходит, верни:");
        sb.AppendLine("{\"function\":\"none\",\"message\":\"...\"}");
        sb.AppendLine();
        sb.AppendLine("Формат ответа: {\"function\":\"<name>\",\"args\":{...}} — аргументы всегда внутри args.");
        sb.AppendLine("Если параметр называется instruction/request/text, его можно не передавать: будет использован весь запрос пользователя.");
        sb.AppendLine("Если нужен IP-адрес, он будет извлечён из запроса автоматически. В параметры IP можно передавать зарегистрированное имя устройства — бэкенд сам найдёт его IP.");
        sb.AppendLine();
        sb.AppendLine("Доступные инструменты (JSON Schema):");
        sb.AppendLine(_schemaText);
        sb.AppendLine();
        sb.AppendLine("Зарегистрированные устройства (имя — IP):");
        var mappings = _ipRegistry.GetAll()
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mappings.Count == 0)
            sb.AppendLine("нет");
        else
            foreach (var m in mappings)
                sb.AppendLine($"- {m.Name} — {m.Ip}");
        sb.AppendLine();
        sb.AppendLine("Запрос пользователя:");
        sb.AppendLine(userInput);
        return sb.ToString();
    }
}
