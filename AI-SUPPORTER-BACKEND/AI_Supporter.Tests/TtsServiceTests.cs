using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

public class TtsServiceTests
{
    [Fact]
    public void TtsSettings_LoadMissing_DefaultsLocalIrina()
    {
        var settings = new TtsSettings();

        settings.Engine.Should().Be("local");
        settings.Voice.Should().Be("");
    }

    [Fact]
    public async Task SynthesizeViaApi_EmptyKey_ReturnsNullFast()
    {
        var llm = new LlmSettings { Api = new ApiLlmConfig { Key = "" } };
        var tts = new TtsService(new TtsSettings(), llm, null, TestHelper.CreateTempDir());

        var wav = await tts.SynthesizeViaApiAsync("привет", null, 1f);

        wav.Should().BeNull();
    }

    [Fact]
    public async Task SynthesizeWithMessage_Api_EmptyKey_Error()
    {
        var llm = new LlmSettings { Api = new ApiLlmConfig { Key = "" } };
        var tts = new TtsService(new TtsSettings(), llm, null, TestHelper.CreateTempDir());

        var (wav, error) = await tts.SynthesizeWithMessageAsync("привет", 1f, "api", null);

        wav.Should().BeNull();
        error.Should().NotBeNull().And.Contain("ключ");
    }

    [Fact]
    public void TtsService_ListLocalVoices_FiltersOnnx()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var models = Path.Combine(dir, "models");
            Directory.CreateDirectory(models);
            File.WriteAllBytes(Path.Combine(models, "a.onnx"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(models, "b.onnx.json"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(models, "c.txt"), new byte[] { 1 });

            var tts = new TtsService(new TtsSettings(), null, null, dir);

            tts.ListLocalVoices().Should().BeEquivalentTo(new[] { "a" });
        }
        finally
        {
            TestHelper.CleanupTempDir(dir);
        }
    }

    [Fact]
    public void TtsService_SetVoice_EmptyUsesDefault()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            var models = Path.Combine(dir, "models");
            Directory.CreateDirectory(models);
            File.WriteAllBytes(Path.Combine(models, "ru_RU-irina-medium.onnx"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(models, "ru_RU-dmitri-medium.onnx"), new byte[] { 1 });

            var tts = new TtsService(new TtsSettings(), null, null, dir);

            tts.SetVoice("");
            tts.CurrentVoice().Should().Be("ru_RU-irina-medium");

            tts.SetVoice("ru_RU-dmitri-medium");
            tts.CurrentVoice().Should().Be("ru_RU-dmitri-medium");
        }
        finally
        {
            TestHelper.CleanupTempDir(dir);
        }
    }
}