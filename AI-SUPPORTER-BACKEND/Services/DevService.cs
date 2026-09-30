using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed record DevHunk(int OldStart, string[] OldLines, int NewStart, string[] NewLines);

public sealed record DevFileChange(string Path, DevHunk[] Hunks);

public sealed record DevChatMessage(string Role, string Text);

public sealed record DevAgentInfo(string Id, string Type);

public sealed class DevChangeProposal
{
    public string? Summary { get; set; }
    public string? Question { get; set; }
    public List<DevFileChange> Changes { get; set; } = new();
}

/// <summary>
/// Режим разработчика: чат с opencode через `opencode run --format json`.
/// opencode НЕ применяет изменения сам — возвращает JSON-предложение, файлы пишет этот сервис
/// только после явного подтверждения. Поддерживаются: выбор агента/модели, продолжение сессии,
/// стриминг размышлений и прогресса, уточняющие вопросы от агента.
/// </summary>
public sealed class DevService
{
    private const string DefaultPassword = "developer";
    private const int MaxHistory = 200;
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly WsEventBus _eventBus;
    private readonly string _projectDir;
    private readonly string _configPath;
    private readonly string _historyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _password;
    private byte[]? _passwordSalt;
    private byte[]? _passwordHash;
    private bool _passwordFromEnv;
    private List<DevChatMessage> _history = new();
    private string? _sessionId;
    private readonly ILogger<DevService>? _logger;

    public DevService(WsEventBus eventBus, ILogger<DevService>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
        _projectDir = FindProjectDir();
        _configPath = Path.Combine(_projectDir, "dev_config.json");
        _historyPath = Path.Combine(_projectDir, "dev_chat_history.json");
        LoadPassword();
        _history = LoadHistory();
    }

    public static readonly JsonSerializerOptions JsonOptions = AppJson.File;

    public string ProjectDir => _projectDir;

    public bool CheckPassword(string password)
    {
        if (_passwordFromEnv)
            return _password == password;
        if (_passwordHash is not null && _passwordSalt is not null)
            return VerifyPassword(password, _passwordSalt, _passwordHash);
        return _password == password;
    }

    public bool CanChangePassword() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_SUPPORTER_DEV_PASSWORD"));

    public bool TryChangePassword(string current, string next, out string error)
    {
        error = string.Empty;
        if (!CheckPassword(current))
        {
            error = "Неверный текущий пароль.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(next) || next.Length < 4)
        {
            error = "Новый пароль слишком короткий (минимум 4 символа).";
            return false;
        }
        try
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = DerivePassword(next, salt);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(new
            {
                algorithm = "PBKDF2-SHA256",
                iterations = Pbkdf2Iterations,
                salt = Convert.ToBase64String(salt),
                hash = Convert.ToBase64String(hash)
            }, JsonOptions));
            _password = null;
            _passwordFromEnv = false;
            _passwordSalt = salt;
            _passwordHash = hash;
        }
        catch (Exception ex)
        {
            error = $"Не удалось сохранить пароль: {ex.Message}";
            return false;
        }
        return true;
    }

    public DevChatMessage[] GetHistory() => _history.ToArray();

    public void ResetHistory()
    {
        _history.Clear();
        _sessionId = null;
        _logger?.LogInformation("[DEV] New chat: история очищена, сессия сброшена");
    }

    public async Task SendPromptAsync(string prompt, string? agent, string? model)
    {
        await _gate.WaitAsync();
        try
        {
            _eventBus.DevThinking();
            AddHistory("user", prompt);
            _logger?.LogInformation("[DEV] Prompt: agent={Agent} model={Model} session={Session} len={Len}", agent ?? "(default)", model ?? "(default)", _sessionId ?? "(new)", prompt.Length);

            var responseText = await RunOpencodeAsync(prompt, agent, model);
            AddHistory("assistant", responseText);

            var proposal = TryParseProposal(responseText);
            if (proposal is not null && !string.IsNullOrWhiteSpace(proposal.Question))
            {
                _logger?.LogInformation("[DEV] Question: {Question}", Truncate(proposal.Question, 200));
                _eventBus.DevResponse($"❓ {proposal.Question}");
            }
            else if (proposal is not null && proposal.Changes.Count > 0)
            {
                var hunks = proposal.Changes.Sum(c => c.Hunks.Length);
                _logger?.LogInformation("[DEV] Proposal: {FileCount} files / {HunkCount} hunks", proposal.Changes.Count, hunks);
                _eventBus.DevChanges(proposal.Changes.ToArray());
                _eventBus.DevResponse(!string.IsNullOrWhiteSpace(proposal.Summary)
                    ? proposal.Summary
                    : "Найдены изменения — проверьте правую панель.");
            }
            else
            {
                _eventBus.DevResponse(responseText);
            }
            _eventBus.DevDone();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEV] Error");
            _eventBus.DevError(ex.Message);
            _eventBus.DevDone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyChangesAsync(DevFileChange[] changes)
    {
        await _gate.WaitAsync();
        try
        {
            _logger?.LogInformation("[DEV] Apply {FileCount} files", changes.Length);
            var report = new StringBuilder();
            foreach (var file in changes)
            {
                var full = Path.GetFullPath(Path.Combine(_projectDir, file.Path.TrimStart('/', '\\')));
                if (!full.StartsWith(_projectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                ApplyHunks(full, file.Hunks);
                report.AppendLine($"✓ {file.Path}");
            }
            _logger?.LogInformation("[DEV] Applied: {Report}", report.Length > 0 ? Truncate(report.ToString().TrimEnd(), 300) : "(empty)");
            _eventBus.DevResponse(report.Length > 0
                ? $"Изменения применены:\n{report.ToString().TrimEnd()}"
                : "Изменения применены.");
            _eventBus.DevApplied();
            _eventBus.DevDone();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEV] Apply error");
            _eventBus.DevError(ex.Message);
            _eventBus.DevDone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RejectChangesAsync()
    {
        _logger?.LogInformation("[DEV] Rejected");
        _eventBus.DevResponse("Изменения отклонены. Ничего не изменено.");
        _eventBus.DevDone();
        await Task.CompletedTask;
    }

    public async Task<(DevAgentInfo[] Agents, string[] Models)> GetOptionsAsync()
    {
        var modelLines = await RunOpencodeArgsAsync(["models"], 15);
        var models = modelLines.Where(l => l.Contains('/')).Distinct().ToArray();

        var agentLines = await RunOpencodeArgsAsync(["agent", "list"], 15);
        var agents = agentLines.Select(ParseAgentLine).Where(a => a is not null).Cast<DevAgentInfo>().ToArray();

        _logger?.LogInformation("[DEV] Options: {AgentCount} agents, {ModelCount} models", agents.Length, models.Length);
        return (agents, models);
    }

    private void AddHistory(string role, string text)
    {
        _history.Add(new DevChatMessage(role, text));
        if (_history.Count > MaxHistory)
            _history.RemoveRange(0, _history.Count - MaxHistory);
        SaveHistory();
    }

    private List<DevChatMessage> LoadHistory()
    {
        try
        {
            if (File.Exists(_historyPath))
            {
                var list = JsonSerializer.Deserialize<List<DevChatMessage>>(File.ReadAllText(_historyPath), JsonOptions);
                if (list is not null) return list;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEV] Failed to load chat history");
        }
        return new List<DevChatMessage>();
    }

    private void SaveHistory()
    {
        try
        {
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(_history, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEV] Failed to save chat history");
        }
    }

    private void LoadPassword()
    {
        var env = Environment.GetEnvironmentVariable("AI_SUPPORTER_DEV_PASSWORD");
        if (!string.IsNullOrWhiteSpace(env))
        {
            _password = env;
            _passwordFromEnv = true;
            return;
        }
        _passwordFromEnv = false;
        try
        {
            if (File.Exists(_configPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
                var root = doc.RootElement;

                if (root.TryGetProperty("salt", out var saltEl) &&
                    root.TryGetProperty("hash", out var hashEl) &&
                    saltEl.ValueKind == JsonValueKind.String &&
                    hashEl.ValueKind == JsonValueKind.String)
                {
                    _passwordSalt = Convert.FromBase64String(saltEl.GetString()!);
                    _passwordHash = Convert.FromBase64String(hashEl.GetString()!);
                    _password = null;
                    return;
                }

                if (root.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String)
                {
                    var legacy = p.GetString()!;
                    var salt = RandomNumberGenerator.GetBytes(SaltSize);
                    var hash = DerivePassword(legacy, salt);
                    _passwordSalt = salt;
                    _passwordHash = hash;
                    _password = null;
                    try
                    {
                        File.WriteAllText(_configPath, JsonSerializer.Serialize(new
                        {
                            algorithm = "PBKDF2-SHA256",
                            iterations = Pbkdf2Iterations,
                            salt = Convert.ToBase64String(salt),
                            hash = Convert.ToBase64String(hash)
                        }, JsonOptions));
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[DEV] Failed to write migrated password config");
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DEV] Failed to load password config");
        }
        _password = DefaultPassword;
    }

    private static byte[] DerivePassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);

    private static bool VerifyPassword(string password, byte[] salt, byte[] expected)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private async Task<string> RunOpencodeAsync(string userPrompt, string? agent, string? model)
    {
        var opencodeExe = FindOpencodeExe();
        if (opencodeExe is null)
        {
            _logger?.LogError("[DEV] opencode не найден в PATH");
            return "opencode не найден в PATH. Установите: npm i -g opencode-ai";
        }

        var psi = new ProcessStartInfo
        {
            FileName = opencodeExe,
            WorkingDirectory = _projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(BuildPrompt(userPrompt));
        if (_sessionId is not null)
        {
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(_sessionId);
        }
        if (!string.IsNullOrWhiteSpace(agent))
        {
            psi.ArgumentList.Add("--agent");
            psi.ArgumentList.Add(agent);
        }
        if (!string.IsNullOrWhiteSpace(model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(model);
        }
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("json");

        _logger?.LogInformation("[DEV] Launching opencode: {Exe}", opencodeExe);

        using var cts = new CancellationTokenSource(RunTimeout);
        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start())
            {
                _logger?.LogError("[DEV] Не удалось запустить opencode");
                return "Не удалось запустить opencode.";
            }
            _logger?.LogInformation("[DEV] PID {Pid}, agent={Agent}, model={Model}, session={Session}", proc.Id, agent ?? "(default)", model ?? "(default)", _sessionId ?? "(new)");

            var reply = new StringBuilder();
            var reasoningChars = 0;
            var errTask = Task.Run(() => proc.StandardError.ReadToEndAsync());

            while (true)
            {
                var line = await proc.StandardOutput.ReadLineAsync(cts.Token);
                if (line is null) break;
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;

                    if (_sessionId is null && root.TryGetProperty("sessionID", out var sid) && sid.ValueKind == JsonValueKind.String)
                        CaptureSession(sid.GetString());

                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                    string? partType = null;
                    string? partText = null;
                    string? partTool = null;
                    var partState = default(JsonElement);
                    if (root.TryGetProperty("part", out var part))
                    {
                        if (part.TryGetProperty("type", out var pt)) partType = pt.GetString();
                        if (part.TryGetProperty("text", out var ptx)) partText = ptx.GetString();
                        if (part.TryGetProperty("tool", out var ptt)) partTool = ptt.GetString();
                        if (part.TryGetProperty("state", out var pts)) partState = pts;
                    }

                    if (type == "tool_use" && partTool is not null)
                    {
                        var detail = BuildToolDetail(partTool, partState);
                        _logger?.LogInformation("[DEV] tool={Tool} {Detail}", partTool, detail);
                        _eventBus.DevThinking(detail);
                    }
                    else if (type == "reasoning" || partType == "reasoning")
                    {
                        if (!string.IsNullOrWhiteSpace(partText))
                        {
                            reasoningChars += partText.Length;
                            _eventBus.DevReasoning(partText);
                        }
                    }
                    else if (type == "text" && partType == "text" && !string.IsNullOrWhiteSpace(partText))
                    {
                        reply.AppendLine(partText);
                    }
                    else if (type == "error")
                    {
                        var err = root.TryGetProperty("error", out var er) ? er.ToString() : "opencode error";
                        _logger?.LogError("[DEV] opencode error: {Error}", Truncate(err, 200));
                        reply.AppendLine($"Ошибка opencode: {err}");
                    }
                }
                catch (JsonException)
                {
                    _logger?.LogError("[DEV] Malformed JSON line from opencode: {Line}", Truncate(trimmed, 120));
                }
            }

            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var errText = await errTask;

            if (reasoningChars > 0)
                _logger?.LogInformation("[DEV] Reasoning streamed: {CharCount} chars", reasoningChars);

            var finalReply = reply.ToString().TrimEnd();
            if (!string.IsNullOrWhiteSpace(finalReply))
            {
                _logger?.LogInformation("[DEV] Reply: {Reply}", Truncate(finalReply, 200));
                return finalReply;
            }
            if (!string.IsNullOrWhiteSpace(errText))
            {
                _logger?.LogError("[DEV] stderr: {Stderr}", Truncate(errText, 300));
                return $"opencode завершился с ошибкой:\n{errText.Trim()}";
            }
            return "opencode не вернул ответ.";
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch (Exception killEx) { _logger?.LogError(killEx, "[DEV] Failed to kill process"); }
            _logger?.LogError("[DEV] Timeout ({Minutes} min), killed pid {Pid}", RunTimeout.TotalMinutes, proc.Id);
            return $"opencode не ответил за {RunTimeout.TotalMinutes:0} минут. Попробуйте ещё раз.";
        }
        catch (Exception ex)
        {
            try { proc.Kill(true); } catch (Exception killEx) { _logger?.LogError(killEx, "[DEV] Failed to kill process"); }
            _logger?.LogError(ex, "[DEV] Error");
            return $"Ошибка при вызове opencode: {ex.Message}";
        }
    }

    private void CaptureSession(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("ses_")) return;
        _sessionId = id;
        _logger?.LogInformation("[DEV] Session captured: {SessionId}", id);
    }

    private static string BuildToolDetail(string? tool, JsonElement state)
    {
        if (string.IsNullOrWhiteSpace(tool)) return string.Empty;
        if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "filePath", "pattern", "command", "query" })
            {
                if (input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString() ?? string.Empty;
                    var name = Path.GetFileName(s);
                    return string.IsNullOrWhiteSpace(name) ? Truncate(s, 60) : name;
                }
            }
        }
        return tool;
    }

    private async Task<string[]> RunOpencodeArgsAsync(IEnumerable<string> args, int timeoutSeconds)
    {
        var opencodeExe = FindOpencodeExe();
        if (opencodeExe is null) return Array.Empty<string>();

        var psi = new ProcessStartInfo
        {
            FileName = opencodeExe,
            WorkingDirectory = _projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return Array.Empty<string>();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var exitTask = proc.WaitForExitAsync();
            var delay = Task.Delay(timeoutSeconds * 1000);
            var completed = await Task.WhenAny(exitTask, delay);
            if (completed == delay)
            {
                try { proc.Kill(true); } catch (Exception killEx) { _logger?.LogError(killEx, "[DEV] Failed to kill process"); }
                return Array.Empty<string>();
            }
            var outText = await stdoutTask;
            return outText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static DevAgentInfo? ParseAgentLine(string line)
    {
        var m = Regex.Match(line.Trim(), @"^([^\s(]+)\s*\(([^)]*)\)");
        if (!m.Success) return null;
        var type = m.Groups[2].Value.Trim();
        return new DevAgentInfo(m.Groups[1].Value, string.IsNullOrEmpty(type) ? "primary" : type);
    }

    private static DevChangeProposal? TryParseProposal(string responseText)
    {
        var start = responseText.IndexOf('{');
        var end = responseText.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        var json = responseText.Substring(start, end - start + 1);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var proposal = new DevChangeProposal
            {
                Summary = root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null,
                Question = root.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String
                    ? q.GetString()
                    : null
            };

            if (root.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in changes.EnumerateArray())
                {
                    if (!item.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String) continue;
                    var hunks = new List<DevHunk>();
                    if (item.TryGetProperty("hunks", out var hunksEl) && hunksEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var h in hunksEl.EnumerateArray())
                        {
                            var oldStart = h.TryGetProperty("oldStart", out var os) ? os.GetInt32() : 0;
                            var newStart = h.TryGetProperty("newStart", out var ns) ? ns.GetInt32() : 0;
                            var oldLines = ReadStringArray(h, "oldLines");
                            var newLines = ReadStringArray(h, "newLines");
                            hunks.Add(new DevHunk(oldStart, oldLines, newStart, newLines));
                        }
                    }
                    proposal.Changes.Add(new DevFileChange(pathEl.GetString()!, hunks.ToArray()));
                }
            }
            return proposal.Changes.Count > 0 || !string.IsNullOrWhiteSpace(proposal.Question) ? proposal : null;
        }
        catch
        {
            return null;
        }
    }

    private static string[] ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
    }

    private string BuildPrompt(string userPrompt)
    {
        return $$"""
            You are DevAssistant, an expert software engineer working in the repository at:
            {{_projectDir}}

            User request:
            {{userPrompt}}

            Task: analyze the repository and PROPOSE the minimal changes that fulfil the request.

            HARD RULES:
            - Do NOT create, edit, write, append, rename or delete any files.
            - Do NOT call the write, edit, patch or bash tools. Use read-only tools only (read, glob, grep, ls).
            - Respond with ONLY a single valid JSON object. No prose, no code fences, no commentary before or after.

            If you need clarification from the developer before proposing changes, respond with ONLY:
            {"question":"your short clarifying question"}
            and nothing else. Otherwise respond with the changes JSON below.

            JSON schema:
            {
              "summary": "short human-readable description of the change",
              "changes": [
                {
                  "path": "relative/path/to/file",
                  "hunks": [
                    {
                      "oldStart": 10,
                      "oldLines": ["exact existing line", "next existing line"],
                      "newStart": 10,
                      "newLines": ["replacement line"]
                    }
                  ]
                }
              ]
            }

            Rules for hunks:
            - oldStart/newStart are 1-based line numbers of the first line of the hunk.
            - oldLines must match the current file content exactly.
            - For a brand-new file: oldLines empty, oldStart 1, newLines = full file content, newStart 1.
            - Every file you propose to change must appear exactly once in "changes".
            - Output the raw JSON object only.
            """;
    }

    private string? FindOpencodeExe()
    {
        var exeName = OperatingSystem.IsWindows() ? "opencode.exe" : "opencode";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var exe = Path.Combine(dir, exeName);
                if (File.Exists(exe)) return exe;
            }
                catch (Exception ex) { _logger?.LogError(ex, "[DEV] PATH scan error"); }
        }
        var npmGlobal = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "opencode-ai", "bin", exeName);
        return File.Exists(npmGlobal) ? npmGlobal : null;
    }

    private static string FindProjectDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AI_Supporter.csproj")))
        {
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        if (dir is null) dir = AppContext.BaseDirectory;
        if (string.Equals(Path.GetFileName(dir), "backend", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(dir)?.FullName ?? dir;
        return dir;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static void ApplyHunks(string path, DevHunk[] hunks)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (!File.Exists(path))
        {
            var lines = hunks.SelectMany(h => h.NewLines).ToArray();
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return;
        }

        var text = File.ReadAllText(path);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var fileLines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var trailingNewline = fileLines.Count > 0 && fileLines[^1] == string.Empty;
        if (trailingNewline) fileLines.RemoveAt(fileLines.Count - 1);

        foreach (var hunk in hunks.OrderByDescending(h => h.OldStart))
        {
            var idx = hunk.OldStart - 1;
            if (idx < 0 || idx + hunk.OldLines.Length > fileLines.Count)
                throw new InvalidOperationException($"Hunk вне границ файла {path} (строка {hunk.OldStart}).");
            for (var i = 0; i < hunk.OldLines.Length; i++)
            {
                if (fileLines[idx + i] != hunk.OldLines[i])
                    throw new InvalidOperationException($"Строка {hunk.OldStart + i} файла {path} не совпадает с предложением.");
            }
            fileLines.RemoveRange(idx, hunk.OldLines.Length);
            fileLines.InsertRange(idx, hunk.NewLines);
        }

        var result = string.Join(newline, fileLines);
        if (result.Length > 0) result += newline;
        File.WriteAllText(path, result, new UTF8Encoding(false));
    }
}
