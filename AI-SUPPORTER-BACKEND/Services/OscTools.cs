using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AiToolProvider]
public sealed class OscTools
{
    private readonly OscService _osc;
    private readonly ILogger<OscTools>? _logger;

    public OscTools(OscService osc, ILogger<OscTools>? logger = null)
    {
        _osc = osc ?? throw new ArgumentNullException(nameof(osc));
        _logger = logger;
    }

    [AiTool("send_osc", "Отправить OSC-команду устройству.")]
    public async Task<string> SendOSC(
        [AiParameter("Полная OSC строка или путь с аргументами")] string command)
    {
        _logger?.LogDebug("[OSC] Send: {Command}", command);
        await _osc.SendAsync(command);
        return $"Отправлена OSC команда: {command}";
    }
}
