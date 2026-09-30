using System.Runtime.CompilerServices;

namespace AI_Supporter;

public sealed class ChatAssistant
{
    private readonly LlmClient _llm;
    private const int MaxContextMessages = 30;
    private const string SystemPrompt =
    """
    Ты — AI-ассистент, специалист по управлению световым, звуковым и сетевым AV-оборудованием.

    Твой профиль:
    - Протоколы: Art-Net, OSC (Open Sound Control), DMX512, sACN, MIDI
    - Оборудование: DMX-контроллеры, LED-панели, медиасерверы, звуковые микшеры
    - Сеть: UDP/TCP на портах 6454 (Art-Net), 8000 (OSC), HTTP API устройств
    - Этот проект: AI Supporter — инструмент для управления и мониторинга AV-оборудования через LLM

    Ты можешь отвечать на вопросы по этой тематике, объяснять протоколы, помогать с настройкой, диагностикой.
    Также ты можешь отвечать на общие вопросы, если они не связаны с твоей специализацией.

    Отвечай естественно, по-русски, без JSON. Если не знаешь ответа — так и скажи.
    """;

    public ChatAssistant(LlmClient llm)
    {
        _llm = llm;
    }

    public async Task<string> AskAsync(string userInput, IReadOnlyList<ChatMessage>? history = null, LlmRequestOptions? options = null, string? imageDataUrl = null)
    {
        var messages = BuildMessages(userInput, history, options, imageDataUrl);
        var response = await _llm.GenerateAsync(messages, options ?? new LlmRequestOptions(Temperature: 0.7, NumPredict: 512));
        var trimmed = response?.Trim() ?? "Не удалось получить ответ.";

        return trimmed;
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(string userInput, IReadOnlyList<ChatMessage>? history = null, LlmRequestOptions? options = null, string? imageDataUrl = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = BuildMessages(userInput, history, options, imageDataUrl);
        await foreach (var chunk in _llm.GenerateStreamingAsync(messages, options ?? new LlmRequestOptions(Temperature: 0.7, NumPredict: 512), ct))
        {
            yield return chunk;
        }
    }

    internal static List<object> BuildMessages(string userInput, IReadOnlyList<ChatMessage>? history, LlmRequestOptions? options = null, string? imageDataUrl = null)
    {
        var messages = new List<object>
        {
            new { role = "system", content = SystemPrompt }
        };

        if (history is not null && history.Count > 0)
        {
            var start = Math.Max(0, history.Count - MaxContextMessages);
            for (var i = start; i < history.Count; i++)
            {
                var msg = history[i];
                if (msg.Role is "user" or "assistant")
                    messages.Add(new { role = msg.Role, content = msg.Text });
            }
        }

        if (string.IsNullOrEmpty(imageDataUrl))
        {
            messages.Add(new { role = "user", content = userInput });
        }
        else if (options?.UseApi == false)
        {
            var rawBase64 = imageDataUrl;
            var idx = rawBase64.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                rawBase64 = rawBase64[(idx + 7)..];
            messages.Add(new { role = "user", content = userInput, images = new[] { rawBase64 } });
        }
        else
        {
            messages.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = userInput },
                    new { type = "image_url", image_url = new { url = imageDataUrl } }
                }
            });
        }
        return messages;
    }
}
