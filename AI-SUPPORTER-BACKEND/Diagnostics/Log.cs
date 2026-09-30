namespace AI_Supporter;

public static class Log
{
    public static readonly bool DebugEnabled =
        string.Equals(Environment.GetEnvironmentVariable("AI_SUPPORTER_DEBUG"), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("AI_SUPPORTER_DEBUG"), "true", StringComparison.OrdinalIgnoreCase);

    public static void Info(string message) => Console.WriteLine(message);

    public static void Debug(string message)
    {
        if (DebugEnabled) Console.WriteLine(message);
    }

    public static void Error(string message) => Console.Error.WriteLine(message);

    public static string Truncate(string? text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return "(null)";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
