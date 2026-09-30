using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

public class LlmProfilePersistenceTests
{
    [Fact]
    public void UserStore_RoundTripsSavedProfile()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            AppPaths.ProjectDir = dir;
            var profile = new UserLlmProfile
            {
                Username = "alice",
                Chat = new TaskModelSelection { Provider = "api", Model = "gpt-4o", AutoSelect = false },
                Voice = new TaskModelSelection { Provider = "local", Model = "qwen2.5:7b", AutoSelect = false },
                Vision = new TaskModelSelection { Provider = "api", Model = "gpt-4o-mini", AutoSelect = true },
                ToolCalling = new TaskModelSelection { Provider = "local", Model = "llama3.1:8b", AutoSelect = false },
                Speech = new SpeechSelection { Provider = "api", Model = "openai/gpt-4o-mini-transcribe", Lang = "en" }
            };
            var store = new UserStore();
            store.SaveLlmProfile("alice", profile);

            var fresh = new UserStore();
            var loaded = fresh.GetLlmProfile("alice");

            loaded.Should().BeEquivalentTo(profile);
            loaded.Chat.Should().BeEquivalentTo(profile.Chat);
            loaded.Voice.Should().BeEquivalentTo(profile.Voice);
            loaded.Vision.Should().BeEquivalentTo(profile.Vision);
            loaded.ToolCalling.Should().BeEquivalentTo(profile.ToolCalling);
            loaded.Speech.Should().BeEquivalentTo(profile.Speech);
        }
        finally
        {
            AppPaths.Reset();
            TestHelper.CleanupTempDir(dir);
        }
    }

    [Fact]
    public void UserStore_MigratesLegacySingleObjectFile()
    {
        var dir = TestHelper.CreateTempDir();
        try
        {
            AppPaths.ProjectDir = dir;
            var legacy = new UserLlmProfile
            {
                Username = "bob",
                Chat = new TaskModelSelection { Provider = "local", Model = "qwen2.5:7b", AutoSelect = true },
                Voice = new TaskModelSelection { Provider = "local", Model = "qwen2.5:7b", AutoSelect = true },
                Vision = new TaskModelSelection { Provider = "api", Model = "gpt-4o", AutoSelect = true },
                ToolCalling = new TaskModelSelection { Provider = "local", Model = "llama3.1:8b", AutoSelect = true }
            };
            File.WriteAllText(Path.Combine(dir, "user_llm_profiles.json"), UserLlmProfileJson.Serialize(legacy));

            var store = new UserStore();
            var loaded = store.GetLlmProfile("bob");

            loaded.Should().BeEquivalentTo(legacy);
            loaded.Vision.Should().BeEquivalentTo(legacy.Vision);
        }
        finally
        {
            AppPaths.Reset();
            TestHelper.CleanupTempDir(dir);
        }
    }

    [Fact]
    public void UserLlmProfileJson_CamelCasePayloadRoundTrips()
    {
        var profile = new UserLlmProfile
        {
            Username = "carol",
            Chat = new TaskModelSelection { Provider = "api", Model = "gpt-4o", AutoSelect = false },
            Vision = new TaskModelSelection { Provider = "local", Model = "llava:13b", AutoSelect = true },
            ToolCalling = new TaskModelSelection { Provider = "api", Model = "gpt-4o-mini", AutoSelect = false },
            Speech = new SpeechSelection { Provider = "api", Model = "openai/gpt-4o-transcribe", Lang = "en" }
        };

        var json = UserLlmProfileJson.Serialize(profile);
        var restored = JsonSerializer.Deserialize<UserLlmProfile>(json, UserLlmProfileJson.Options);

        restored.Should().NotBeNull();
        restored!.Username.Should().Be("carol");
        restored.Chat.Should().BeEquivalentTo(profile.Chat);
        restored.Vision.Should().BeEquivalentTo(profile.Vision);
        restored.ToolCalling.Should().BeEquivalentTo(profile.ToolCalling);
        restored.Speech.Should().BeEquivalentTo(profile.Speech);
    }
}