using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class ChatHistoryService
{
    private const int MaxHistory = 200;
    private readonly ConcurrentDictionary<string, List<ConversationMeta>> _userIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _messages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ChatHistoryService>? _logger;
    private readonly string _baseDir;

    public ChatHistoryService(string? baseDir = null, ILogger<ChatHistoryService>? logger = null)
    {
        _baseDir = baseDir ?? AppPaths.ProjectDir;
        _logger = logger;
    }

    private SemaphoreSlim GateFor(string key) => _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private string IndexPath(string user) => Path.Combine(_baseDir, $"chat_index_{user}.json");
    private string MessagesPath(string user, string convId) => Path.Combine(_baseDir, $"chat_{user}_{convId}.json");
    private string LegacyPath(string user) => Path.Combine(_baseDir, $"chat_history_{user}.json");

    public IReadOnlyList<ConversationMeta> ListConversations(string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return Array.Empty<ConversationMeta>();
        var gate = GateFor($"idx:{user}");
        gate.Wait();
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            return list.ToArray();
        }
        finally { gate.Release(); }
    }

    public ConversationMeta CreateConversation(string user, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("user is required");
        var convId = $"conv_{Guid.NewGuid().ToString("N")[..8]}";
        var now = DateTime.UtcNow;
        var meta = new ConversationMeta(convId, title ?? "Новый чат", now, now);
        var gate = GateFor($"idx:{user}");
        gate.Wait();
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            list.Add(meta);
            SaveIndex(user, list);
        }
        finally { gate.Release(); }
        _messages[ConvKey(user, convId)] = new List<ChatMessage>();
        return meta;
    }

    public ChatMessage[] GetHistory(string user, string? conversationId = null)
    {
        if (string.IsNullOrWhiteSpace(user)) return Array.Empty<ChatMessage>();
        var convId = conversationId ?? GetLastConversationId(user);
        if (convId is null) return Array.Empty<ChatMessage>();
        var gate = GateFor(ConvKey(user, convId));
        gate.Wait();
        try
        {
            var list = _messages.GetOrAdd(ConvKey(user, convId), _ => LoadMessages(user, convId));
            return list.ToArray();
        }
        finally { gate.Release(); }
    }

    public void Add(string user, string conversationId, string role, string text, string? image = null)
    {
        if (string.IsNullOrWhiteSpace(user) || (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(image))) return;
        var gate = GateFor(ConvKey(user, conversationId));
        gate.Wait();
        try
        {
            var list = _messages.GetOrAdd(ConvKey(user, conversationId), _ => LoadMessages(user, conversationId));
            list.Add(new ChatMessage(role, text, conversationId, DateTime.UtcNow, image));
            if (list.Count > MaxHistory)
                list.RemoveRange(0, list.Count - MaxHistory);
            SaveMessages(user, conversationId, list);
        }
        finally { gate.Release(); }
        UpdateLastMessageAt(user, conversationId);
    }

    public void Reset(string user, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(conversationId)) return;
        var gate = GateFor(ConvKey(user, conversationId));
        gate.Wait();
        try
        {
            _messages[ConvKey(user, conversationId)] = new List<ChatMessage>();
            try { File.Delete(MessagesPath(user, conversationId)); }
            catch (Exception ex) { _logger?.LogError(ex, "[CHAT] Failed to delete messages for {User}/{Conv}", user, conversationId); }
        }
        finally { gate.Release(); }
    }

    public bool DeleteConversation(string user, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(conversationId)) return false;
        var idxGate = GateFor($"idx:{user}");
        idxGate.Wait();
        bool removed;
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            removed = list.RemoveAll(m => m.Id == conversationId) > 0;
            SaveIndex(user, list);
        }
        finally { idxGate.Release(); }
        var msgKey = ConvKey(user, conversationId);
        _messages.TryRemove(msgKey, out _);
        try { File.Delete(MessagesPath(user, conversationId)); }
        catch { }
        return removed;
    }

    public bool RenameConversation(string user, string conversationId, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(conversationId)) return false;
        var gate = GateFor($"idx:{user}");
        gate.Wait();
        bool found;
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            var idx = list.FindIndex(m => m.Id == conversationId);
            if (idx < 0) { found = false; }
            else
            {
                list[idx] = list[idx] with { Title = newTitle };
                SaveIndex(user, list);
                found = true;
            }
        }
        finally { gate.Release(); }
        return found;
    }

    public string? GetLastConversationId(string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return null;
        var gate = GateFor($"idx:{user}");
        gate.Wait();
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            return list.Count > 0 ? list[^1].Id : null;
        }
        finally { gate.Release(); }
    }

    // Backward-compatible methods: redirect to "default" conversation

    public ChatMessage[] GetHistory(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return Array.Empty<ChatMessage>();
        EnsureDefault(username);
        var lastConv = GetLastConversationId(username);
        return lastConv is not null ? GetHistory(username, lastConv) : Array.Empty<ChatMessage>();
    }

    public void Add(string username, string role, string text)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(text)) return;
        var convId = EnsureDefault(username);
        Add(username, convId, role, text);
    }

    public void Reset(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        var convId = EnsureDefault(username);
        Reset(username, convId);
    }

    private string EnsureDefault(string user)
    {
        MigrateIfNeeded(user);

        // Атомарное создание default-конверсации: check-then-create в одной критической секции
        // защищает от гонок при параллельных вызовах Add (без вложенного захвата того же gate).
        var gate = GateFor($"idx:{user}");
        gate.Wait();
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            if (list.Count > 0) return list[^1].Id;

            var convId = $"conv_{Guid.NewGuid().ToString("N")[..8]}";
            var now = DateTime.UtcNow;
            var meta = new ConversationMeta(convId, "Основной чат", now, now);
            list.Add(meta);
            SaveIndex(user, list);
            _messages[ConvKey(user, convId)] = new List<ChatMessage>();
            return meta.Id;
        }
        finally { gate.Release(); }
    }

    private void MigrateIfNeeded(string user)
    {
        var legacyPath = LegacyPath(user);
        if (!File.Exists(legacyPath)) return;
        try
        {
            var legacy = System.Text.Json.JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(legacyPath), DevService.JsonOptions);
            if (legacy is null || legacy.Count == 0) { try { File.Delete(legacyPath); } catch { } return; }
            var meta = CreateConversation(user, "Основной чат");
            var gate = GateFor(ConvKey(user, meta.Id));
            gate.Wait();
            try
            {
                var list = legacy.Select(m => new ChatMessage(m.Role, m.Text, meta.Id, DateTime.UtcNow)).ToList();
                _messages[ConvKey(user, meta.Id)] = list;
                SaveMessages(user, meta.Id, list);
            }
            finally { gate.Release(); }
            try { File.Delete(legacyPath); }
            catch { }
            _logger?.LogInformation("[CHAT] Migrated legacy history for {User}", user);
        }
        catch (Exception ex) { _logger?.LogError(ex, "[CHAT] Migration failed for {User}", user); }
    }

    private void UpdateLastMessageAt(string user, string convId)
    {
        var gate = GateFor($"idx:{user}");
        gate.Wait();
        try
        {
            var list = _userIndex.GetOrAdd(user, u => LoadIndex(u));
            var idx = list.FindIndex(m => m.Id == convId);
            if (idx >= 0)
                list[idx] = list[idx] with { LastMessageAt = DateTime.UtcNow };
            SaveIndex(user, list);
        }
        finally { gate.Release(); }
    }

    private static string ConvKey(string user, string convId) => $"{user}:{convId}";

    private List<ConversationMeta> LoadIndex(string user)
    {
        try
        {
            var path = IndexPath(user);
            if (File.Exists(path))
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<ConversationMeta>>(File.ReadAllText(path), DevService.JsonOptions);
                if (list is not null) return list;
            }
        }
        catch (Exception ex) { _logger?.LogError(ex, "[CHAT] Failed to load index for {User}", user); }
        return new List<ConversationMeta>();
    }

    private void SaveIndex(string user, List<ConversationMeta> list)
    {
        try
        {
            File.WriteAllText(IndexPath(user), System.Text.Json.JsonSerializer.Serialize(list, DevService.JsonOptions));
        }
        catch (Exception ex) { _logger?.LogError(ex, "[CHAT] Failed to save index for {User}", user); }
    }

    private List<ChatMessage> LoadMessages(string user, string convId)
    {
        try
        {
            var path = MessagesPath(user, convId);
            if (File.Exists(path))
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(path), DevService.JsonOptions);
                if (list is not null)
                {
                    if (list.Count > MaxHistory) list.RemoveRange(0, list.Count - MaxHistory);
                    return list;
                }
            }
        }
        catch (Exception ex) { _logger?.LogError(ex, "[CHAT] Failed to load messages for {User}/{Conv}", user, convId); }
        return new List<ChatMessage>();
    }

    private void SaveMessages(string user, string convId, List<ChatMessage> list)
    {
        try
        {
            File.WriteAllText(MessagesPath(user, convId), System.Text.Json.JsonSerializer.Serialize(list, DevService.JsonOptions));
        }
        catch (Exception ex) { _logger?.LogError(ex, "[CHAT] Failed to save messages for {User}/{Conv}", user, convId); }
    }
}
