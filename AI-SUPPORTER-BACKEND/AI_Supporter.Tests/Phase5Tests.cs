using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI_Supporter.Tests;

/// <summary>
/// Phase 5 tests: DI container wiring, service resolution.
/// </summary>
public class Phase5Tests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        ServiceRegistrar.Configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Container_Builds_WithoutErrors()
    {
        var provider = BuildProvider();
        await provider.DisposeAsync();
    }

    [Theory]
    [InlineData(typeof(UserStore))]
    [InlineData(typeof(ChatHistoryService))]
    [InlineData(typeof(LlmSettings))]
    [InlineData(typeof(LlmClient))]
    [InlineData(typeof(ConfigEditor))]
    [InlineData(typeof(ChatAssistant))]
    [InlineData(typeof(IpRegistryService))]
    [InlineData(typeof(TrafficSnifferService))]
    [InlineData(typeof(TrafficStorageService))]
    [InlineData(typeof(SseService))]
    [InlineData(typeof(WsEventBus))]
    [InlineData(typeof(TrafficAnalyzerService))]
    [InlineData(typeof(TrafficReportService))]
    [InlineData(typeof(ArtnetService))]
    [InlineData(typeof(OscService))]
    [InlineData(typeof(OscServerService))]
    [InlineData(typeof(TaskRegistryService))]
    [InlineData(typeof(DevService))]
    [InlineData(typeof(WhisperService))]
    [InlineData(typeof(WakeWordService))]
    [InlineData(typeof(TtsService))]
    [InlineData(typeof(DeviceTestService))]
    [InlineData(typeof(SystemStatsService))]
    [InlineData(typeof(ToolRegistry))]
    [InlineData(typeof(ArtNetTools))]
    [InlineData(typeof(OscTools))]
    [InlineData(typeof(NetworkTools))]
    [InlineData(typeof(DeviceTools))]
    [InlineData(typeof(ConfigTools))]
    [InlineData(typeof(AdminTools))]
    [InlineData(typeof(NavigationTools))]
    [InlineData(typeof(FileBrowserService))]
    [InlineData(typeof(AppInitializer))]
    [InlineData(typeof(ServiceLifetimeManager))]
    public async Task Resolve_ReturnsNonNull(Type serviceType)
    {
        var provider = BuildProvider();
        var service = provider.GetService(serviceType);
        service.Should().NotBeNull($"{serviceType.Name} should be resolvable");
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Singleton_ReturnsSameInstance()
    {
        var provider = BuildProvider();
        var a1 = provider.GetRequiredService<IpRegistryService>();
        var a2 = provider.GetRequiredService<IpRegistryService>();
        a1.Should().BeSameAs(a2);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task ToolRegistry_HasTools()
    {
        var provider = BuildProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        var tools = registry.GetAll();
        tools.Should().HaveCountGreaterThan(0);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task EventBus_IsSingleton()
    {
        var provider = BuildProvider();
        var e1 = provider.GetRequiredService<WsEventBus>();
        var e2 = provider.GetRequiredService<WsEventBus>();
        e1.Should().BeSameAs(e2);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task LlmSettings_IsSingleton()
    {
        var provider = BuildProvider();
        var s1 = provider.GetRequiredService<LlmSettings>();
        var s2 = provider.GetRequiredService<LlmSettings>();
        s1.Should().BeSameAs(s2);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task MultipleResolves_DoNotThrow()
    {
        var provider = BuildProvider();
        var act = async () =>
        {
            _ = provider.GetRequiredService<UserStore>();
            _ = provider.GetRequiredService<ChatHistoryService>();
            _ = provider.GetRequiredService<LlmClient>();
            _ = provider.GetRequiredService<IpRegistryService>();
            _ = provider.GetRequiredService<TrafficSnifferService>();
            _ = provider.GetRequiredService<SseService>();
            _ = provider.GetRequiredService<WsEventBus>();
            _ = provider.GetRequiredService<TrafficAnalyzerService>();
            _ = provider.GetRequiredService<ArtnetService>();
            _ = provider.GetRequiredService<OscService>();
            _ = provider.GetRequiredService<OscServerService>();
            _ = provider.GetRequiredService<TaskRegistryService>();
            _ = provider.GetRequiredService<DevService>();
            _ = provider.GetRequiredService<WhisperService>();
            _ = provider.GetRequiredService<WakeWordService>();
            _ = provider.GetRequiredService<TtsService>();
            _ = provider.GetRequiredService<DeviceTestService>();
            _ = provider.GetRequiredService<SystemStatsService>();
            _ = provider.GetRequiredService<ToolRegistry>();
            _ = provider.GetRequiredService<AppInitializer>();
            await provider.DisposeAsync();
        };
        await act.Should().NotThrowAsync();
    }
}
