namespace AI_Supporter;

public sealed record ChatMessage(string Role, string Text, string? ConversationId = null, DateTime Timestamp = default, string? Image = null);
public sealed record ConversationMeta(string Id, string Title, DateTime CreatedAt, DateTime LastMessageAt);
