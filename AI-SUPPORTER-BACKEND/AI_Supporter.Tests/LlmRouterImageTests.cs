using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

public class LlmRouterImageTests
{
    private const string ClosedPortUrl = "http://127.0.0.1:1/api/generate";

    [Fact]
    public async Task LlmClient_PingProviderModel_ApiWithEmptyKey_ReturnsFalseWithoutNetwork()
    {
        var settings = new LlmSettings { Provider = "local" };
        settings.Api.Key = "";
        var client = new LlmClient(settings);

        var (ok, ms, model) = await client.PingAsync("api", "gpt-4o");

        ok.Should().BeFalse();
        ms.Should().Be(0);
        model.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task LlmClient_PingProviderModel_LocalUnreachable_ReturnsFalseQuickly()
    {
        Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", ClosedPortUrl);
        try
        {
            var settings = new LlmSettings { Provider = "local" };
            settings.Local.Url = ClosedPortUrl;
            var client = new LlmClient(settings);

            var (ok, _, model) = await client.PingAsync("local", "qwen2.5:7b");

            ok.Should().BeFalse();
            model.Should().Be("qwen2.5:7b");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", null);
        }
    }

    [Fact]
    public async Task LlmClient_GetAvailableLocalModels_ReturnsNullWhenOllamaUnreachable()
    {
        Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", ClosedPortUrl);
        try
        {
            var settings = new LlmSettings { Provider = "api" };
            settings.Local.Url = ClosedPortUrl;
            var client = new LlmClient(settings);

            var models = await client.GetAvailableLocalModelsAsync();

            models.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", null);
        }
    }

    [Fact]
    public async Task LlmClient_GetAvailableApiModels_EmptyKey_ReturnsNull()
    {
        var settings = new LlmSettings { Provider = "local" };
        settings.Api.Key = "";
        var client = new LlmClient(settings);

        var models = await client.GetAvailableApiModelsAsync();

        models.Should().BeNull();
    }

    [Fact]
    public async Task LlmClient_GetAvailableApiModels_UnreachableBaseUrl_ReturnsNullFast()
    {
        var settings = new LlmSettings { Provider = "local" };
        settings.Api.Key = "test-key";
        settings.Api.BaseUrl = "http://127.0.0.1:1/openai/v1";
        var client = new LlmClient(settings);

        var models = await client.GetAvailableApiModelsAsync();

        models.Should().BeNull();
    }

    [Fact]
    public async Task LlmRouter_GetCapabilitiesDtoAsync_EmptyKey_ApiNull()
    {
        var dir = TestHelper.CreateTempDir();
        Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", ClosedPortUrl);
        try
        {
            AppPaths.ProjectDir = dir;
            var settings = new LlmSettings { Provider = "local" };
            settings.Local.Url = ClosedPortUrl;
            var client = new LlmClient(settings);
            var store = new UserStore();
            var router = new LlmRouter(client, client, store, settings);

            var caps = await router.GetCapabilitiesDtoAsync();

            caps.Api.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", null);
            AppPaths.Reset();
            TestHelper.CleanupTempDir(dir);
        }
    }

    [Fact]
    public async Task LlmRouter_ResolveBriefAsync_UnknownTaskDefaultsToChatLocal()
    {
        var dir = TestHelper.CreateTempDir();
        Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", ClosedPortUrl);
        try
        {
            AppPaths.ProjectDir = dir;
            var settings = new LlmSettings { Provider = "local" };
            settings.Local.Url = ClosedPortUrl;
            var client = new LlmClient(settings);
            var store = new UserStore();
            var router = new LlmRouter(client, client, store, settings);

            var (provider, model, _) = await router.ResolveBriefAsync("unknown-task", "alice");

            provider.Should().Be("local");
            model.Should().Be(settings.EffectiveLocalModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", null);
            AppPaths.Reset();
            TestHelper.CleanupTempDir(dir);
        }
    }

    [Fact]
    public void BuildMessages_ImageWithApiShape_UsesOpenAiContentArray()
    {
        var messages = ChatAssistant.BuildMessages("что на фото?", null, new LlmRequestOptions(UseApi: true), "data:image/png;base64,AAAA");

        messages.Should().HaveCount(2);
        var user = JsonSerializer.SerializeToElement(messages[^1]);
        user.GetProperty("role").GetString().Should().Be("user");
        var content = user.GetProperty("content");
        content.ValueKind.Should().Be(JsonValueKind.Array);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().Be("что на фото?");
        content[1].GetProperty("type").GetString().Should().Be("image_url");
        content[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be("data:image/png;base64,AAAA");
    }

    [Fact]
    public void BuildMessages_ImageWithNullOptions_UsesOpenAiShape()
    {
        var messages = ChatAssistant.BuildMessages("что на фото?", null, null, "data:image/png;base64,AAAA");

        var user = JsonSerializer.SerializeToElement(messages[^1]);
        user.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void BuildMessages_ImageWithOllamaShape_UsesNativeImages()
    {
        var messages = ChatAssistant.BuildMessages("что на фото?", null, new LlmRequestOptions(UseApi: false), "data:image/jpeg;base64,QUJD");

        messages.Should().HaveCount(2);
        var user = JsonSerializer.SerializeToElement(messages[^1]);
        user.GetProperty("role").GetString().Should().Be("user");
        user.GetProperty("content").GetString().Should().Be("что на фото?");
        var images = user.GetProperty("images");
        images.ValueKind.Should().Be(JsonValueKind.Array);
        images[0].GetString().Should().Be("QUJD");
    }

    [Fact]
    public void BuildMessages_ImageOllamaShape_RawBase64WithoutPrefixPassedThrough()
    {
        var messages = ChatAssistant.BuildMessages("что на фото?", null, new LlmRequestOptions(UseApi: false), "QUJDRA==");

        var user = JsonSerializer.SerializeToElement(messages[^1]);
        user.GetProperty("images")[0].GetString().Should().Be("QUJDRA==");
    }
}