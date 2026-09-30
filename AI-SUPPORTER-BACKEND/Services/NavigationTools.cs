using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class NavigationTools
{
    private static readonly string[] Screens =
    {
        "home", "voice", "chat", "network", "system", "terminal", "files", "tasks", "settings", "dev"
    };

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["дом"] = "home",
        ["главная"] = "home",
        ["главный экран"] = "home",
        ["старт"] = "home",
        ["голос"] = "voice",
        ["голосовой"] = "voice",
        ["голосовой помощник"] = "voice",
        ["микрофон"] = "voice",
        ["чат"] = "chat",
        ["ассистент"] = "chat",
        ["общение"] = "chat",
        ["сеть"] = "network",
        ["сетевой"] = "network",
        ["трафик"] = "network",
        ["сетевой трафик"] = "network",
        ["система"] = "system",
        ["системный"] = "system",
        ["терминал"] = "terminal",
        ["консоль"] = "terminal",
        ["командная строка"] = "terminal",
        ["файлы"] = "files",
        ["файловый"] = "files",
        ["проводник"] = "files",
        ["задачи"] = "tasks",
        ["задания"] = "tasks",
        ["скрипты"] = "tasks",
        ["настройки"] = "settings",
        ["параметры"] = "settings",
        ["конфигурация"] = "settings",
        ["разработка"] = "dev",
        ["разработчик"] = "dev",
        ["дев"] = "dev"
    };

    private readonly WsEventBus _eventBus;
    private readonly ILogger<NavigationTools>? _logger;

    public NavigationTools(WsEventBus eventBus, ILogger<NavigationTools>? logger = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    [AiTool(
        "navigate_to",
        "Открыть экран интерфейса. Поддерживает русские синонимы. Допустимые экраны: " +
        "home (главная/дом), voice (голос), chat (чат), network (сеть/трафик), system (система), " +
        "terminal (терминал), files (файлы), tasks (задачи), settings (настройки), dev (разработка).")]
    public Task<string> NavigateTo(
        [AiParameter("Экран или его русский синоним: home, voice, chat, network, system, terminal, files, tasks, settings, dev (или «настройки», «сеть», «терминал» и т.п.)")] string screen)
    {
        var key = Resolve(screen);
        if (key is null)
        {
            _logger?.LogWarning("[NAV] navigate_to: неизвестный экран «{Screen}»", screen);
            return Task.FromResult(
                $"Неизвестный экран «{screen}». Допустимые: {string.Join(", ", Screens)}. " +
                "Попросите пользователя уточнить, либо используйте один из допустимых.");
        }

        _logger?.LogDebug("[NAV] navigate_to: {Screen} → {Key}", screen, key);
        _eventBus.Navigate(key);
        return Task.FromResult($"Открываю экран «{key}».");
    }

    private static string? Resolve(string screen)
    {
        var value = screen?.Trim()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(value)) return null;
        if (Screens.Contains(value)) return value;
        return Synonyms.TryGetValue(value, out var key) ? key : null;
    }
}
