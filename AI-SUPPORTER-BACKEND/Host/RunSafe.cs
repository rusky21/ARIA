namespace AI_Supporter;

/// <summary>
/// Wraps fire-and-forget tasks with exception safety.
/// Suppresses OperationCanceledException (normal shutdown);
/// logs all other exceptions to Console.Error.
/// </summary>
public static class RunSafe
{
    /// <summary>
    /// Fire-and-forget wrapper for long-running loops that accept a CancellationToken.
    /// </summary>
    public static void Run(Func<CancellationToken, Task> taskFactory, CancellationToken ct, string name = "")
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await taskFactory(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogError(name, ex);
            }
        });
    }

    /// <summary>
    /// Fire-and-forget wrapper for one-shot async tasks.
    /// </summary>
    public static void Run(Func<Task> taskFactory, string name = "")
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await taskFactory();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogError(name, ex);
            }
        });
    }

    private static void LogError(string name, Exception ex)
    {
        var label = string.IsNullOrWhiteSpace(name) ? "RunSafe" : name;
        Console.Error.WriteLine($"[{label}] Unhandled exception: {ex.Message}");
    }
}
