using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

public class WhisperAsrTests
{
    [Fact]
    public async Task TranscribeAsync_ApiEmptyKey_ReturnsNullFast()
    {
        var settings = new LlmSettings { Provider = "api", Api = new ApiLlmConfig { Key = "" } };
        await using var svc = new WhisperService(settings);

        var result = await svc.TranscribeAsync(Array.Empty<byte>(), "api", null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeWithMessage_ApiEmptyKey_ReturnsError()
    {
        var settings = new LlmSettings { Provider = "api", Api = new ApiLlmConfig { Key = "" } };
        await using var svc = new WhisperService(settings);

        var (text, error) = await svc.TranscribeWithMessageAsync(Array.Empty<byte>(), "api", null, null);

        text.Should().BeNull();
        error.Should().NotBeNull().And.Contain("ключ");
    }

    [Fact]
    public async Task CheckEngine_ApiEmptyKey_False()
    {
        var settings = new LlmSettings { Api = new ApiLlmConfig { Key = "  " } };
        await using var svc = new WhisperService(settings);

        var (ok, engine, message) = svc.CheckEngine("api", null);

        ok.Should().BeFalse();
        engine.Should().Be("API");
        message.Should().Be("API-ключ не задан");
    }

    [Fact]
    public async Task CheckEngine_ApiKeySet_True()
    {
        var settings = new LlmSettings { Api = new ApiLlmConfig { Key = "secret" } };
        await using var svc = new WhisperService(settings);

        var (ok, engine, message) = svc.CheckEngine("api", "openai/gpt-4o-mini-transcribe");

        ok.Should().BeTrue();
        engine.Should().Be("API");
        message.Should().Be("Доступен");
    }

    [Fact]
    public void CheckEngine_Local_BinaryAbsent_False()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var svc = new WhisperService(null, null, null, Path.Combine(dir, "whisper"));

            var (ok, engine, message) = svc.CheckEngine("local", "tiny");

            ok.Should().BeFalse();
            engine.Should().Be("whisper.cpp");
            message.Should().Be("Движок whisper.cpp не установлен");
        }
        finally
        {
            TestHelper.CleanupTempDir(dir);
        }
    }
}