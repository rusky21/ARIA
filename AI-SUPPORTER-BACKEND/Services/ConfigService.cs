namespace AI_Supporter;

/// <summary>
/// Адаптер к редактору конфигурации устройства.
/// </summary>
public sealed class ConfigService
{
    private readonly Func<string, Task> _editConfig;

    public ConfigService(Func<string, Task> editConfig)
    {
        _editConfig = editConfig ?? throw new ArgumentNullException(nameof(editConfig));
    }

    public Task EditAsync(string instruction)
    {
        return _editConfig(instruction);
    }
}
