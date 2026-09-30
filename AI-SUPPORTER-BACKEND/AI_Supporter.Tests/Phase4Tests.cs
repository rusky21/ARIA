using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

/// <summary>
/// Phase 4 tests: extracted service logic (TestDataService, DeviceTestService).
/// </summary>
public class Phase4Tests
{
    // ── TestDataService ────────────────────────────────────────

    [Theory]
    [InlineData("192.168.1.100", true)]
    [InlineData("192.168.1.50", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.0.0.2", true)]
    [InlineData("172.16.0.10", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("192.168.1.1", false)]
    public void IsSimulatedIp_ReturnsExpected(string ip, bool expected)
    {
        TestDataService.IsSimulatedIp(ip).Should().Be(expected);
    }

    [Theory]
    [InlineData("192.168.1.100")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.10")]
    public void IsSimulatedIp_IsCaseInsensitive(string ip)
    {
        TestDataService.IsSimulatedIp(ip.ToUpperInvariant()).Should().BeTrue();
    }

    [Fact]
    public void GetSimulatedDevices_ReturnsAllFiveSimulatedIps()
    {
        var ipRegistry = new IpRegistryService();
        var devices = TestDataService.GetSimulatedDevices(ipRegistry);
        devices.Should().HaveCount(5);
    }

    [Fact]
    public void GetSimulatedDevices_UsesIpRegistryNames()
    {
        var ipRegistry = new IpRegistryService();
        ipRegistry.Register("192.168.1.100", "My DMX");
        var devices = TestDataService.GetSimulatedDevices(ipRegistry);
        var dmx = devices.FirstOrDefault(d => d.ip == "192.168.1.100");
        dmx.name.Should().Be("My DMX");
    }

    [Fact]
    public void GetSimulatedDevices_ReturnsNameFromIpRegistry()
    {
        var ipRegistry = new IpRegistryService();
        ipRegistry.Register("192.168.1.100", "Custom DMX");
        var devices = TestDataService.GetSimulatedDevices(ipRegistry);
        var dmx = devices.FirstOrDefault(d => d.ip == "192.168.1.100");
        dmx.name.Should().Be("Custom DMX");
    }

    [Fact]
    public void RegisterSimulatedDevices_RegistersAllFiveIps()
    {
        var ipRegistry = new IpRegistryService();
        TestDataService.RegisterSimulatedDevices(ipRegistry);
        ipRegistry.Lookup("192.168.1.100").Should().Be("DMX Controller");
        ipRegistry.Lookup("192.168.1.50").Should().Be("OSC Mixer");
        ipRegistry.Lookup("10.0.0.1").Should().Be("Lighting Console");
        ipRegistry.Lookup("10.0.0.2").Should().Be("LED Panels");
        ipRegistry.Lookup("172.16.0.10").Should().Be("Media Server");
    }

    [Fact]
    public void GenerateTestData_AddsRecordsToSniffer()
    {
        var ipRegistry = new IpRegistryService();
        using var sniffer = new TrafficSnifferService(ipRegistry);

        TestDataService.GenerateTestData(sniffer, ipRegistry);

        var recent = sniffer.GetRecent(1000);
        recent.Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public void GenerateTestData_RegistersDevicesInIpRegistry()
    {
        var ipRegistry = new IpRegistryService();
        using var sniffer = new TrafficSnifferService(ipRegistry);

        TestDataService.GenerateTestData(sniffer, ipRegistry);

        ipRegistry.Lookup("192.168.1.100").Should().Be("ArtNet Controller");
        ipRegistry.Lookup("10.0.0.1").Should().Be("Lighting Console");
        ipRegistry.Lookup("10.10.10.10").Should().Be("Unknown Device");
    }

    [Fact]
    public void GenerateTestData_IncludesMultipleProtocols()
    {
        var ipRegistry = new IpRegistryService();
        using var sniffer = new TrafficSnifferService(ipRegistry);

        TestDataService.GenerateTestData(sniffer, ipRegistry);

        var records = sniffer.GetRecent(1000);
        var protocols = records.Select(r => r.Protocol).Distinct().ToList();
        protocols.Should().Contain("ARTNET");
        protocols.Should().Contain("OSC");
        protocols.Should().Contain("TCP");
        protocols.Should().Contain("UDP");
    }

    // ── DeviceTestService ──────────────────────────────────────

    [Fact]
    public async Task DeviceTest_PingRealIp_AttemptsPing()
    {
        var svc = new DeviceTestService(new ArtnetService(), new OscService(), new OscServerService());
        var results = await svc.RunTestsAsync("127.0.0.1", new[] { "ping" });
        results.Should().HaveCount(1);
        results[0].test.Should().Be("ping");
    }

    [Fact]
    public async Task DeviceTest_PingSimulatedIp_ReturnsSuccess()
    {
        var svc = new DeviceTestService(new ArtnetService(), new OscService(), new OscServerService());
        var results = await svc.RunTestsAsync("192.168.1.100", new[] { "ping" });
        results.Should().HaveCount(1);
        results[0].success.Should().BeTrue();
    }

    [Fact]
    public async Task DeviceTest_HttpSimulatedIp_ReturnsSuccess()
    {
        var svc = new DeviceTestService(new ArtnetService(), new OscService(), new OscServerService());
        var results = await svc.RunTestsAsync("192.168.1.100", new[] { "http" });
        results.Should().HaveCount(1);
        results[0].success.Should().BeTrue();
    }

    [Fact]
    public async Task DeviceTest_ArtnetSimulatedIp_ReturnsSuccess()
    {
        var svc = new DeviceTestService(new ArtnetService(), new OscService(), new OscServerService());
        var results = await svc.RunTestsAsync("192.168.1.100", new[] { "artnet" });
        results.Should().HaveCount(1);
        results[0].success.Should().BeTrue();
    }

    [Fact]
    public async Task DeviceTest_OscWithNullServices_ReturnsError()
    {
        var svc = new DeviceTestService(new ArtnetService(), null!, null!);
        var results = await svc.RunTestsAsync("192.168.1.100", new[] { "osc" });
        results.Should().HaveCount(1);
        results[0].success.Should().BeFalse();
    }

    [Fact]
    public async Task DeviceTest_MultipleTests_ReturnsAllResults()
    {
        var svc = new DeviceTestService(new ArtnetService(), new OscService(), new OscServerService());
        var results = await svc.RunTestsAsync("192.168.1.100", new[] { "ping", "http", "artnet" });
        results.Should().HaveCount(3);
        results.Select(r => r.test).Should().Contain(new[] { "ping", "http", "artnet" });
    }

    [Fact]
    public async Task DeviceTest_EmptyTests_ReturnsEmpty()
    {
        var svc = new DeviceTestService(new ArtnetService(), new OscService(), new OscServerService());
        var results = await svc.RunTestsAsync("192.168.1.100", Array.Empty<string>());
        results.Should().BeEmpty();
    }
}
