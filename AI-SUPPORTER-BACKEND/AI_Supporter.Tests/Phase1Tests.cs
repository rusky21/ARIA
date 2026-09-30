using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

/// <summary>
/// Phase 1 tests: disposal, graceful shutdown, thread safety, async patterns.
/// </summary>
public class Phase1Tests : IDisposable
{
    private readonly string _tempDir;

    public Phase1Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ai_test_p1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── ServiceLifetimeManager ─────────────────────────────────────

    [Fact]
    public async Task ServiceLifetimeManager_DisposesSyncServices()
    {
        var disposed = false;
        var manager = new ServiceLifetimeManager();
        manager.Track(new TestDisposable(() => disposed = true));

        await manager.DisposeAsync();

        disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceLifetimeManager_DisposesAsyncServices()
    {
        var disposed = false;
        var manager = new ServiceLifetimeManager();
        manager.Track(new TestAsyncDisposable(() => disposed = true));

        await manager.DisposeAsync();

        disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceLifetimeManager_DisposesInReverseOrder()
    {
        var order = new List<int>();
        var manager = new ServiceLifetimeManager();
        manager.Track(new TestDisposable(() => order.Add(1)));
        manager.Track(new TestDisposable(() => order.Add(2)));
        manager.Track(new TestDisposable(() => order.Add(3)));

        await manager.DisposeAsync();

        order.Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task ServiceLifetimeManager_DoubleDispose_IsIdempotent()
    {
        var count = 0;
        var manager = new ServiceLifetimeManager();
        manager.Track(new TestDisposable(() => count++));

        await manager.DisposeAsync();
        await manager.DisposeAsync();

        count.Should().Be(1);
    }

    [Fact]
    public async Task ServiceLifetimeManager_ExceptionInDispose_DoesNotStopOthers()
    {
        var disposed = false;
        var manager = new ServiceLifetimeManager();
        manager.Track(new TestDisposable(() => throw new InvalidOperationException("boom")));
        manager.Track(new TestDisposable(() => disposed = true));

        await manager.DisposeAsync();

        disposed.Should().BeTrue();
    }

    // ── TrafficSnifferService disposal ─────────────────────────────

    [Fact]
    public void TrafficSniffer_Stop_DisposesCts()
    {
        var ipRegistry = new IpRegistryService();
        var sniffer = new TrafficSnifferService(ipRegistry);

        // Start then stop should not throw
        sniffer.Stop();
        sniffer.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void TrafficSniffer_Dispose_CallsStop()
    {
        var ipRegistry = new IpRegistryService();
        var sniffer = new TrafficSnifferService(ipRegistry);

        var act = () => sniffer.Dispose();
        act.Should().NotThrow();
        sniffer.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void TrafficSniffer_Stop_MultipleCallsAreIdempotent()
    {
        var ipRegistry = new IpRegistryService();
        var sniffer = new TrafficSnifferService(ipRegistry);

        sniffer.Stop();
        sniffer.Stop();
        sniffer.IsRunning.Should().BeFalse();
    }

    // ── OscServerService disposal ──────────────────────────────────

    [Fact]
    public void OscServer_Stop_DisposesCts()
    {
        var server = new OscServerService();

        // Start then stop should not throw
        server.Stop();
        server.LastReceived.Should().BeNull();
    }

    [Fact]
    public void OscServer_Dispose_CallsStop()
    {
        var server = new OscServerService();

        var act = () => server.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void OscServer_Stop_MultipleCallsAreIdempotent()
    {
        var server = new OscServerService();

        server.Stop();
        server.Stop(); // should not throw
    }

    // ── TaskRegistryService disposal ───────────────────────────────

    [Fact]
    public void TaskRegistry_Dispose_CancelsTasks()
    {
        var ipRegistry = new IpRegistryService();
        var sniffer = new TrafficSnifferService(ipRegistry);
        var artnet = new ArtnetService();
        var oscServer = new OscServerService();
        var sse = new SseService();
        var eventBus = new WsEventBus(sse);

        var registry = new TaskRegistryService(sniffer, artnet, oscServer, ipRegistry, eventBus);

        var act = () => registry.Dispose();
        act.Should().NotThrow();
    }

    // ── ChatHistoryService async ───────────────────────────────────

    [Fact]
    public async Task ChatHistory_AddAsync_Completes()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var testUser = $"test_async_{Guid.NewGuid():N}";

            await Task.Run(() => svc.Add(testUser, "user", "hello"));

            var history = svc.GetHistory(testUser);
            history.Should().HaveCount(1);
            history[0].Text.Should().Be("hello");
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    [Fact]
    public async Task ChatHistory_ResetAsync_Completes()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var testUser = $"test_reset_{Guid.NewGuid():N}";
            svc.Add(testUser, "user", "hello");

            await Task.Run(() => svc.Reset(testUser));

            var history = svc.GetHistory(testUser);
            history.Should().BeEmpty();
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    [Fact]
    public async Task ChatHistory_AddAsync_ConcurrentAccess_NoCorruption()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var testUser = $"test_concurrent_{Guid.NewGuid():N}";

            var tasks = Enumerable.Range(0, 10)
                .Select(i => Task.Run(() => svc.Add(testUser, "user", $"msg{i}")))
                .ToArray();

            await Task.WhenAll(tasks);

            var history = svc.GetHistory(testUser);
            history.Should().HaveCount(10);
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    // ── Helpers ────────────────────────────────────────────────────

    private sealed class TestDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    private sealed class TestAsyncDisposable(Action onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }
}
