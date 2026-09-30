using Microsoft.Extensions.Logging;

namespace AI_Supporter;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAdminAttribute : Attribute
{
    public string? Role { get; }
    public RequireAdminAttribute(string? role = null) => Role = role;
}

[AiToolProvider]
public sealed class AdminTools
{
    private readonly ILogger<AdminTools>? _logger;

    public AdminTools(ILogger<AdminTools>? logger = null)
    {
        _logger = logger;
    }

    [AiTool("request_feature_development",
        "Запросить разработку новой функции. Используй, когда функциональность не реализована текущими инструментами.")]
    [RequireAdmin]
    public async Task<string> RequestFeatureDevelopment(
        [AiParameter("Описание запрашиваемой функции")] string description,
        [AiParameter("Приоритет: low/medium/high")] string priority = "medium")
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger?.LogInformation("[ADMIN] Feature request #{RequestId} [{Priority}]: {Description}",
            requestId, priority, description[..Math.Min(description.Length, 100)]);
        await File.AppendAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "feature_requests.log"),
            $"[{DateTime.Now:O}] [{requestId}] [{priority}] {description}{Environment.NewLine}");
        return $"Запрос #{requestId} принят. Приоритет: {priority}.";
    }
}
