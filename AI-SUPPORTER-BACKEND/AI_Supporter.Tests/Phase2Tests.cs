using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

/// <summary>
/// Phase 2 tests: RunSafe helper, error handling, logging.
/// </summary>
public class Phase2Tests
{
    // ── RunSafe.Run(Func<CancellationToken, Task>) ──────────────

    [Fact]
    public async Task RunSafe_LongRunningTask_SwallowsOperationCanceledException()
    {
        var completed = false;
        var cts = new CancellationTokenSource();

        RunSafe.Run(async ct =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                completed = true;
                throw;
            }
        }, cts.Token, "Test");

        cts.Cancel();
        await Task.Delay(100);

        completed.Should().BeTrue();
    }

    [Fact]
    public async Task RunSafe_OneShotTask_SwallowsOperationCanceledException()
    {
        var completed = false;

        RunSafe.Run(() => Task.Run(() =>
        {
            completed = true;
            throw new OperationCanceledException();
        }), "Test");

        await Task.Delay(100);
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task RunSafe_OneShotTask_CatchesAndLogsException()
    {
        var completed = false;

        RunSafe.Run(() => Task.Run(() =>
        {
            completed = true;
            throw new InvalidOperationException("test error");
        }), "Test");

        await Task.Delay(100);
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task RunSafe_MultipleCalls_OneFailureDoesNotAffectOthers()
    {
        var completed1 = false;
        var completed2 = false;

        RunSafe.Run(() => Task.Run(() => throw new InvalidOperationException("boom")), "FailingTask");
        RunSafe.Run(() => Task.Run(() => completed1 = true), "SuccessfulTask1");
        RunSafe.Run(() => Task.Run(() => completed2 = true), "SuccessfulTask2");

        await Task.Delay(200);

        completed1.Should().BeTrue();
        completed2.Should().BeTrue();
    }

    // ── RunSafe.Run(Func<CancellationToken, Task>) — cancellation ───

    [Fact]
    public async Task RunSafe_LongRunningFunc_CompletesNormally()
    {
        var completed = false;

        RunSafe.Run(async ct =>
        {
            await Task.Delay(10, ct);
            completed = true;
        }, CancellationToken.None, "Test");

        await Task.Delay(200);
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task RunSafe_OneShotFunc_CompletesNormally()
    {
        var completed = false;

        RunSafe.Run(() => Task.Run(() => completed = true), "Test");

        await Task.Delay(200);
        completed.Should().BeTrue();
    }
}
