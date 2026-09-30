namespace AI_Supporter;

public static class AppPaths
{
    private static string? _projectDir;

    public static string ProjectDir
    {
        get
        {
            if (_projectDir is not null) return _projectDir;
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "AI_Supporter.csproj")))
            {
                var parent = Directory.GetParent(dir);
                dir = parent?.FullName;
            }
            if (dir is null) dir = AppContext.BaseDirectory;
            if (string.Equals(Path.GetFileName(dir), "backend", StringComparison.OrdinalIgnoreCase))
                dir = Directory.GetParent(dir)?.FullName ?? dir;
            _projectDir = dir;
            return _projectDir;
        }
        set => _projectDir = value;
    }

    public static string UsersFile => Path.Combine(ProjectDir, "users.json");

    public static string ChatHistoryFile(string username) =>
        Path.Combine(ProjectDir, $"chat_history_{username}.json");

    public static void Reset()
    {
        _projectDir = null;
    }
}
