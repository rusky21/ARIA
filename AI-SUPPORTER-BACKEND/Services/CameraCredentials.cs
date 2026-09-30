namespace AI_Supporter;

/// <summary>
/// Креды для камер (Axis VAPIX и т.п.), используемые при обнаружении и MJPEG-проверке.
/// Берутся из переменных окружения AI_SUPPORTER_CAM_USER / AI_SUPPORTER_CAM_PASS.
/// Если не заданы — возвращается значение по умолчанию (root/root), НЕ хардкод в коде.
/// </summary>
public static class CameraCredentials
{
    public static string User => Environment.GetEnvironmentVariable("AI_SUPPORTER_CAM_USER") ?? "root";
    public static string Pass => Environment.GetEnvironmentVariable("AI_SUPPORTER_CAM_PASS") ?? "root";
}
