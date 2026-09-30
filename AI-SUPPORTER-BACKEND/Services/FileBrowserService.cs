using System.Text;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed record FileEntry(string Name, string Path, bool IsDir, long Size, DateTime? Modified);
public sealed record DirListing(string Path, string? Parent, IReadOnlyList<FileEntry> Entries);
public sealed record FileContent(string? Content, bool Truncated, long Size, bool Binary);

public sealed class FileBrowserService
{
    private readonly string _root;
    private readonly ILogger<FileBrowserService>? _logger;
    private const long MaxReadBytes = 262144;

    public FileBrowserService(ILogger<FileBrowserService>? logger = null)
    {
        _logger = logger;
        _root = Path.GetFullPath(
            Environment.GetEnvironmentVariable("AI_SUPPORTER_FILES_ROOT")
            ?? Path.Combine(AppPaths.ProjectDir, "files"));
        try { Directory.CreateDirectory(_root); }
        catch (Exception ex) { _logger?.LogError(ex, "[FILES] Failed to create root directory"); }
    }

    public string Root => _root;

    public DirListing List(string? requestedPath)
    {
        var full = Resolve(requestedPath);
        var parent = full == _root ? null : Path.GetDirectoryName(full);

        if (!Directory.Exists(full))
            return new DirListing(full, parent, Array.Empty<FileEntry>());

        var entries = new List<FileEntry>();
        foreach (var p in Directory.EnumerateFileSystemEntries(full))
        {
            try
            {
                var isDir = Directory.Exists(p);
                var name = Path.GetFileName(p);
                long size = 0;
                DateTime? modified = null;
                if (isDir)
                {
                    modified = new DirectoryInfo(p).LastWriteTimeUtc;
                }
                else
                {
                    var fi = new FileInfo(p);
                    size = fi.Length;
                    modified = fi.LastWriteTimeUtc;
                }
                entries.Add(new FileEntry(name, p, isDir, size, modified));
            }
            catch (Exception ex) { _logger?.LogError(ex, "[FILES] Failed to read entry metadata"); }
        }

        entries.Sort((a, b) =>
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new DirListing(full, parent, entries);
    }

    public FileContent? Read(string? requestedPath)
    {
        var full = Resolve(requestedPath);
        if (!File.Exists(full)) return null;

        var info = new FileInfo(full);
        using var fs = File.OpenRead(full);
        if (fs.Length == 0)
            return new FileContent("", false, 0, false);

        if (fs.Length > MaxReadBytes)
            fs.Seek(-MaxReadBytes, SeekOrigin.End);

        var toRead = Math.Min(fs.Length, MaxReadBytes);
        var buf = new byte[toRead];
        fs.ReadExactly(buf, 0, buf.Length);

        var binary = Array.IndexOf(buf, (byte)0) >= 0;
        if (binary)
            return new FileContent(null, info.Length > MaxReadBytes, info.Length, true);

        var text = Encoding.UTF8.GetString(buf).TrimStart('\uFEFF');
        return new FileContent(text, info.Length > MaxReadBytes, info.Length, false);
    }

    private string Resolve(string? requestedPath)
    {
        var rel = requestedPath ?? "";
        if (string.IsNullOrWhiteSpace(rel)) return _root;

        var full = Path.GetFullPath(Path.Combine(_root, rel));
        var rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (full != _root && !full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Выход за пределы корня запрещён");

        return full;
    }
}
