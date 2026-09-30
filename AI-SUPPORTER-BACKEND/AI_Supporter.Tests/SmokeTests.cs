using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

/// <summary>
/// Smoke-тесты: проверяем что сервисы создаются без падений и базовые методы работают.
/// </summary>
public class SmokeTests : IDisposable
{
    private readonly string _tempDir;

    public SmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ai_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── IpRegistryService ──────────────────────────────────────────

    [Fact]
    public void IpRegistry_CanCreate()
    {
        var svc = new IpRegistryService();
        svc.Should().NotBeNull();
    }

    [Fact]
    public void IpRegistry_RegisterAndLookup()
    {
        var svc = new IpRegistryService();
        svc.Register("192.168.1.100", "TestDevice");
        var name = svc.Lookup("192.168.1.100");
        name.Should().Be("TestDevice");
    }

    [Fact]
    public void IpRegistry_LookupUnknownIp_ReturnsNull()
    {
        var svc = new IpRegistryService();
        var name = svc.Lookup("10.0.0.99");
        name.Should().BeNull();
    }

    [Fact]
    public void IpRegistry_GetAll_ReturnsList()
    {
        var svc = new IpRegistryService();
        var all = svc.GetAll();
        all.Should().NotBeNull();
    }

    [Fact]
    public void IpRegistry_ResolveToIp_ReturnsIpAsIs()
    {
        var svc = new IpRegistryService();
        svc.ResolveToIp("192.168.1.50").Should().Be("192.168.1.50");
    }

    [Fact]
    public void IpRegistry_ResolveToIp_ByName()
    {
        var svc = new IpRegistryService();
        svc.Register("192.168.1.100", "TestDevice");
        svc.ResolveToIp("TestDevice").Should().Be("192.168.1.100");
    }

    [Fact]
    public void IpRegistry_ResolveToIp_CaseInsensitive_AndTrimmed()
    {
        var svc = new IpRegistryService();
        svc.Register("192.168.1.100", "TestDevice");
        svc.ResolveToIp("  testdevice  ").Should().Be("192.168.1.100");
    }

    [Fact]
    public void IpRegistry_ResolveToIp_UnknownName_ReturnsNull()
    {
        var svc = new IpRegistryService();
        svc.ResolveToIp("NoSuchDevice").Should().BeNull();
    }

    [Fact]
    public void IpRegistry_GetByName_CaseInsensitive()
    {
        var svc = new IpRegistryService();
        svc.Register("192.168.1.100", "TestDevice");
        var entry = svc.GetByName("TESTDEVICE");
        entry.Should().NotBeNull();
        entry!.Ip.Should().Be("192.168.1.100");
    }

    // ── ArtnetService ──────────────────────────────────────────────

    [Fact]
    public void Artnet_CanCreate()
    {
        var svc = new ArtnetService();
        svc.Should().NotBeNull();
    }

    // ── OscService ─────────────────────────────────────────────────

    [Fact]
    public void Osc_CanCreate()
    {
        var svc = new OscService();
        svc.Should().NotBeNull();
    }

    // ── DeviceService ──────────────────────────────────────────────

    [Fact]
    public async Task Device_RefreshAsync_Completes()
    {
        var svc = new DeviceService();
        var act = () => svc.RefreshAsync("192.168.1.1");
        await act.Should().NotThrowAsync();
    }

    // ── ChatHistoryService ─────────────────────────────────────────

    [Fact]
    public void ChatHistory_GetHistory_EmptyUser_ReturnsEmpty()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var history = svc.GetHistory("");
            history.Should().BeEmpty();
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    [Fact]
    public void ChatHistory_GetHistory_UnknownUser_ReturnsEmpty()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var history = svc.GetHistory("nonexistent_user_12345");
            history.Should().BeEmpty();
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    [Fact]
    public void ChatHistory_AddAndRetrieve()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var testUser = $"test_user_{Guid.NewGuid():N}";
            svc.Add(testUser, "user", "hello");
            svc.Add(testUser, "assistant", "hi there");
            var history = svc.GetHistory(testUser);
            history.Should().HaveCount(2);
            history[0].Role.Should().Be("user");
            history[0].Text.Should().Be("hello");
            history[1].Role.Should().Be("assistant");
            history[1].Text.Should().Be("hi there");
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    [Fact]
    public void ChatHistory_AddEmptyText_DoesNotAdd()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new ChatHistoryService(dir);
            var testUser = $"test_user_{Guid.NewGuid():N}";
            svc.Add(testUser, "user", "");
            var history = svc.GetHistory(testUser);
            history.Should().BeEmpty();
        }
        finally { TestHelper.CleanupTempDir(dir); }
    }

    // ── UserStore ──────────────────────────────────────────────────

    [Fact]
    public void UserStore_CreatesAdminOnFirstRun()
    {
        var path = Path.Combine(_tempDir, "users.json");
        var store = new UserStore(path);
        store.Should().NotBeNull();
        store.IsAdmin("admin").Should().BeTrue();
    }

    [Fact]
    public void UserStore_AdminCanAuthenticate()
    {
        var path = Path.Combine(_tempDir, "users.json");
        var store = new UserStore(path);
        var ok = store.Authenticate("admin", "admin", out var account);
        ok.Should().BeTrue();
        account.Should().NotBeNull();
        account!.Username.Should().Be("admin");
        account.Role.Should().Be("admin");
    }

    [Fact]
    public void UserStore_WrongPassword_Fails()
    {
        var path = Path.Combine(_tempDir, "users.json");
        var store = new UserStore(path);
        var ok = store.Authenticate("admin", "wrongpassword", out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void UserStore_CreateAndFindUser()
    {
        var path = Path.Combine(_tempDir, "users.json");
        var store = new UserStore(path);
        var created = store.CreateUser("testuser", "pass1234", "user", out var error);
        created.Should().BeTrue();
        error.Should().BeEmpty();

        var found = store.Find("testuser");
        found.Should().NotBeNull();
        found!.Username.Should().Be("testuser");
        found.Role.Should().Be("user");
    }

    [Fact]
    public void UserStore_IsValidUsername()
    {
        UserStore.IsValidUsername("admin").Should().BeTrue();
        UserStore.IsValidUsername("test_user").Should().BeTrue();
        UserStore.IsValidUsername("user.name").Should().BeTrue();
        UserStore.IsValidUsername("").Should().BeFalse();
        UserStore.IsValidUsername(null).Should().BeFalse();
        UserStore.IsValidUsername("user name").Should().BeFalse();
    }

    [Fact]
    public void UserStore_GetAll_ReturnsArray()
    {
        var path = Path.Combine(_tempDir, "users.json");
        var store = new UserStore(path);
        var all = store.GetAll();
        all.Should().NotBeEmpty();
        all.Should().Contain(u => u.Username == "admin");
    }

    // ── FileBrowserService ─────────────────────────────────────────

    [Fact]
    public void FileBrowser_CanCreate()
    {
        var svc = new FileBrowserService();
        svc.Should().NotBeNull();
        svc.Root.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FileBrowser_ListRoot_ReturnsEntries()
    {
        var svc = new FileBrowserService();
        var listing = svc.List(null);
        listing.Should().NotBeNull();
        listing.Entries.Should().NotBeNull();
    }

    // ── TrafficStorageService (construct only) ─────────────────────

    [Fact]
    public void TrafficStorage_CanCreate()
    {
        var svc = new TrafficStorageService();
        svc.Should().NotBeNull();
    }

    // ── RequestContext ─────────────────────────────────────────────

    [Fact]
    public void RequestContext_Current_IsNullByDefault()
    {
        RequestContext.Current.Value.Should().BeNull();
    }

    [Fact]
    public void RequestContext_Current_CanSetAndGet()
    {
        RequestContext.Current.Value = "test_user";
        RequestContext.Current.Value.Should().Be("test_user");
        RequestContext.Current.Value = null; // cleanup
    }

    // ── VulnerabilityCache ─────────────────────────────────────────

    [Fact]
    public void VulnerabilityCache_HasDefaultValues()
    {
        VulnerabilityCache.HasVulnerabilities.Should().BeFalse();
        VulnerabilityCache.Details.Should().BeNull();
    }
}
