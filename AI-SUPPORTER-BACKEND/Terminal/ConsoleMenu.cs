namespace AI_Supporter;

public static class ConsoleMenu
{
    public static async Task RunBackendAsync(IServiceProvider? services = null)
    {
        var logBuffer = new LogBuffer(Console.Out);
        Console.SetOut(logBuffer);
        Console.SetError(logBuffer);

        var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        RunSafe.Run(() => KeyListenerAsync(logBuffer, shutdownCts), "ConsoleMenu.KeyListener");

        try
        {
            if (services is not null)
                await AppHost.RunFromDiAsync([], services, shutdownCts.Token);
            else
                await AppHost.RunAsync([], shutdownCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Console.WriteLine("[SHUTDOWN] Shutting down...");
            shutdownCts.Dispose();
        }
    }

    private static async Task KeyListenerAsync(LogBuffer logBuffer, CancellationTokenSource shutdownCts)
    {
        while (!shutdownCts.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Spacebar)
                    {
                        logBuffer.TogglePause();
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        shutdownCts.Cancel();
                        return;
                    }
                }

                if (logBuffer.IsPaused)
                {
                    var cursorLeft = Console.CursorLeft;
                    var cursorTop = Console.CursorTop;
                    var fg = Console.ForegroundColor;

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"\r[PAUSED] Space — продолжить | Ctrl+Q — выход");

                    Console.ForegroundColor = fg;
                    Console.SetCursorPosition(cursorLeft, cursorTop);
                }

                await Task.Delay(200, shutdownCts.Token);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
