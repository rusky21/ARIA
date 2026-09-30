using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace AI_Supporter.Tests;

public class ChatAssistantTests
{
    [Fact]
    public void BuildMessages_NoHistory_SystemThenUser()
    {
        var messages = ChatAssistant.BuildMessages("привет", null);
        messages.Should().HaveCount(2);
        var first = JsonSerializer.SerializeToElement(messages[0]);
        var last = JsonSerializer.SerializeToElement(messages[1]);
        first.GetProperty("role").GetString().Should().Be("system");
        last.GetProperty("role").GetString().Should().Be("user");
        last.GetProperty("content").GetString().Should().Be("привет");
    }

    [Fact]
    public void BuildMessages_WithHistory_KeepsOrder()
    {
        var history = new[]
        {
            new ChatMessage("user", "вопрос 1"),
            new ChatMessage("assistant", "ответ 1"),
            new ChatMessage("user", "вопрос 2"),
        };
        var messages = ChatAssistant.BuildMessages("вопрос 3", history);
        messages.Should().HaveCount(5);
        var contents = messages
            .Select(m => JsonSerializer.SerializeToElement(m))
            .Select(m => $"{m.GetProperty("role").GetString()}:{m.GetProperty("content").GetString()}")
            .ToArray();
        contents.Should().ContainInOrder(
            "user:вопрос 1",
            "assistant:ответ 1",
            "user:вопрос 2",
            "user:вопрос 3");
    }

    [Fact]
    public void BuildMessages_LongHistory_TrimsToLast30()
    {
        var history = new List<ChatMessage>();
        for (var i = 0; i < 50; i++)
            history.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", $"msg {i}"));
        var messages = ChatAssistant.BuildMessages("финальный", history);
        messages.Should().HaveCount(32);
        var last = JsonSerializer.SerializeToElement(messages[^1]);
        last.GetProperty("content").GetString().Should().Be("финальный");
        var firstHistory = JsonSerializer.SerializeToElement(messages[1]);
        firstHistory.GetProperty("content").GetString().Should().Be("msg 20");
    }

    [Fact]
    public void BuildMessages_SkipsNonUserAssistantRoles()
    {
        var history = new[]
        {
            new ChatMessage("system", "не должно попадать"),
            new ChatMessage("user", "реальный вопрос"),
        };
        var messages = ChatAssistant.BuildMessages("новый", history);
        messages.Should().HaveCount(3);
        var contents = messages
            .Select(m => JsonSerializer.SerializeToElement(m))
            .Select(m => m.GetProperty("content").GetString())
            .ToArray();
        contents.Should().NotContain("не должно попадать");
    }
}