using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class ConfigTools
{
    private readonly ConfigService _config;
    private readonly ILogger<ConfigTools>? _logger;

    public ConfigTools(ConfigService config, ILogger<ConfigTools>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    [AiTool("edit_config", "Изменить GUI/настройки устройства по запросу (например, цвет кнопок или подписи).")]
    public async Task<string> EditConfig(
        [AiParameter("Полный текст запроса пользователя")] string instruction)
    {
        _logger?.LogDebug("[CONFIG] Edit: {Instruction}", instruction[..Math.Min(instruction.Length, 80)]);
        await _config.EditAsync(instruction);
        return $"Настройки изменены: {instruction}";
    }
}
