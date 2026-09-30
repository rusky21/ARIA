using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class UserAccount
{
    public string Username { get; set; } = "";
    public string Role { get; set; } = "user";
    public byte[] Salt { get; set; } = Array.Empty<byte>();
    public byte[] Hash { get; set; } = Array.Empty<byte>();
    public long CreatedAtUtc { get; set; }
    public bool MustChangePassword { get; set; }
}

/// <summary>
/// Многопользовательское хранилище учётных записей (users.json).
/// Пароли — PBKDF2-SHA256 (как DevService). При первом запуске создаётся admin/admin.
/// </summary>
public sealed class UserStore
{
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MinPasswordLength = 4;
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9_.-]{1,32}$");

    private static readonly object Gate = new();
    private readonly string _path;
    private readonly Dictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<UserStore>? _logger;

    /// <summary>Глобальный экземпляр, устанавливается при старте (ручной DI).</summary>
    public static UserStore? Current { get; private set; }

    public UserStore(string? path = null, ILogger<UserStore>? logger = null)
    {
        _logger = logger;
        _path = path ?? Path.Combine(AppPaths.ProjectDir, "users.json");
        Load();
        if (_users.Count == 0)
        {
            CreateUser("admin", "admin", "admin", out _);
            if (_users.TryGetValue("admin", out var admin))
            {
                admin.MustChangePassword = true;
                Save();
                _logger?.LogWarning("[USERS] Создан учётная запись по умолчанию admin/admin. Требуется смена пароля.");
            }
        }
        Current = this;
    }

    public static bool IsValidUsername(string? username) =>
        !string.IsNullOrWhiteSpace(username) && UsernamePattern.IsMatch(username);

    public bool Authenticate(string username, string password, out UserAccount? account)
    {
        account = null;
        if (!IsValidUsername(username)) return false;
        lock (Gate)
        {
            if (!_users.TryGetValue(username, out var user)) return false;
            if (!VerifyPassword(password, user.Salt, user.Hash)) return false;
            account = user;
            return true;
        }
    }

    public UserAccount? Find(string username)
    {
        lock (Gate)
            return _users.TryGetValue(username, out var user) ? Clone(user) : null;
    }

    public bool IsAdmin(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        lock (Gate)
            return _users.TryGetValue(username, out var user) && user.Role == "admin";
    }

    public UserAccount[] GetAll()
    {
        lock (Gate)
            return _users.Values.Select(Clone).OrderBy(u => u.Username).ToArray();
    }

    public bool CreateUser(string username, string password, string role, out string error)
    {
        error = string.Empty;
        if (!IsValidUsername(username))
        {
            error = "Недопустимое имя пользователя (A-Za-z0-9_.-, до 32 символов).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            error = $"Пароль слишком короткий (минимум {MinPasswordLength} символа).";
            return false;
        }
        if (role is not ("admin" or "user"))
        {
            error = "Роль должна быть admin или user.";
            return false;
        }

        lock (Gate)
        {
            if (_users.ContainsKey(username))
            {
                error = "Пользователь уже существует.";
                return false;
            }
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = DerivePassword(password, salt);
            _users[username] = new UserAccount
            {
                Username = username,
                Role = role,
                Salt = salt,
                Hash = hash,
                CreatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
        Save();
        _logger?.LogInformation("[USERS] Created user '{Username}' role={Role}", username, role);
        return true;
    }

    public bool UpdateRole(string username, string role, out string error)
    {
        error = string.Empty;
        if (role is not ("admin" or "user"))
        {
            error = "Роль должна быть admin или user.";
            return false;
        }
        lock (Gate)
        {
            if (!_users.TryGetValue(username, out var user))
            {
                error = "Пользователь не найден.";
                return false;
            }
            if (user.Role == "admin" && role == "user" && CountAdmins() <= 1)
            {
                error = "Нельзя понизить последнего администратора.";
                return false;
            }
            user.Role = role;
        }
        Save();
        _logger?.LogInformation("[USERS] Role updated: '{Username}' -> {Role}", username, role);
        return true;
    }

    public bool Delete(string username, out string error)
    {
        error = string.Empty;
        lock (Gate)
        {
            if (!_users.TryGetValue(username, out var user))
            {
                error = "Пользователь не найден.";
                return false;
            }
            if (user.Role == "admin" && CountAdmins() <= 1)
            {
                error = "Нельзя удалить последнего администратора.";
                return false;
            }
            _users.Remove(username);
        }
        Save();
        _logger?.LogInformation("[USERS] Deleted user '{Username}'", username);
        return true;
    }

    public bool ChangePassword(string username, string current, string next, out string error)
    {
        error = string.Empty;
        lock (Gate)
        {
            if (!_users.TryGetValue(username, out var user)) { error = "Пользователь не найден."; return false; }
            if (!VerifyPassword(current, user.Salt, user.Hash)) { error = "Неверный текущий пароль."; return false; }
            if (string.IsNullOrWhiteSpace(next) || next.Length < MinPasswordLength)
            {
                error = $"Новый пароль слишком короткий (минимум {MinPasswordLength} символа).";
                return false;
            }
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                error = "Новый пароль не должен совпадать с текущим.";
                return false;
            }
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            user.Salt = salt;
            user.Hash = DerivePassword(next, salt);
            user.MustChangePassword = false;
        }
        Save();
        return true;
    }

    private int CountAdmins()
    {
        lock (Gate)
            return _users.Values.Count(u => u.Role == "admin");
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<UserAccount>>(File.ReadAllText(_path), DevService.JsonOptions);
                if (list is not null)
                {
                    foreach (var u in list)
                    {
                        if (IsValidUsername(u.Username) && u.Hash is { Length: > 0 } && u.Salt is { Length: > 0 })
                            _users[u.Username] = u;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[USERS] Ошибка чтения users.json");
        }
    }

    private void Save()
    {
        lock (Gate)
        {
            try
            {
                var list = _users.Values.Select(u => new
                {
                    username = u.Username,
                    role = u.Role,
                    salt = Convert.ToBase64String(u.Salt),
                    hash = Convert.ToBase64String(u.Hash),
                    createdAtUtc = u.CreatedAtUtc,
                    mustChangePassword = u.MustChangePassword
                }).ToList();
                File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(list, DevService.JsonOptions));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[USERS] Ошибка сохранения users.json");
            }
        }
    }

    private static UserAccount Clone(UserAccount u) => new()
    {
        Username = u.Username,
        Role = u.Role,
        Salt = u.Salt,
        Hash = u.Hash,
        CreatedAtUtc = u.CreatedAtUtc,
        MustChangePassword = u.MustChangePassword
    };

    private static byte[] DerivePassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);

    private static bool VerifyPassword(string password, byte[] salt, byte[] expected)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // ── LLM Profile per user ─────────────────────────────────────────

    private const string LlmProfilePath = "user_llm_profiles.json";
    private readonly Dictionary<string, UserLlmProfile> _llmProfiles = new(StringComparer.OrdinalIgnoreCase);

    public UserLlmProfile GetLlmProfile(string username)
    {
        lock (Gate)
        {
            if (_llmProfiles.TryGetValue(username, out var profile))
                return profile;

            // Load from file if not cached
            LoadLlmProfiles();
            if (_llmProfiles.TryGetValue(username, out profile))
                return profile;

            // Create default from global settings
            return CreateDefaultProfile(username);
        }
    }

    public bool SaveLlmProfile(string username, UserLlmProfile profile)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        profile.Username = username;
        lock (Gate)
        {
            _llmProfiles[username] = profile;
            return SaveLlmProfiles();
        }
    }

    private void LoadLlmProfiles()
    {
        try
        {
            var path = Path.Combine(AppPaths.ProjectDir, LlmProfilePath);
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            List<UserLlmProfile>? profiles = null;
            try
            {
                profiles = System.Text.Json.JsonSerializer.Deserialize<List<UserLlmProfile>>(json, UserLlmProfileJson.Options);
            }
            catch
            {
                profiles = null;
            }

            if (profiles is not null)
            {
                foreach (var profile in profiles)
                {
                    if (!string.IsNullOrWhiteSpace(profile.Username))
                        _llmProfiles[profile.Username] = profile;
                }
                return;
            }

            var single = UserLlmProfileJson.Deserialize(json);
            if (single is not null && !string.IsNullOrWhiteSpace(single.Username))
                _llmProfiles[single.Username] = single;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[USERS] Ошибка чтения LLM профилей");
        }
    }

    private bool SaveLlmProfiles()
    {
        try
        {
            var path = Path.Combine(AppPaths.ProjectDir, LlmProfilePath);
            var list = _llmProfiles.Values.ToList();
            var jsonArray = System.Text.Json.JsonSerializer.Serialize(list, UserLlmProfileJson.Options);
            File.WriteAllText(path, jsonArray);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[USERS] Ошибка сохранения LLM профилей");
            return false;
        }
    }

    private UserLlmProfile CreateDefaultProfile(string username)
    {
        var profile = new UserLlmProfile { Username = username };
        _llmProfiles[username] = profile;
        return profile;
    }
}
