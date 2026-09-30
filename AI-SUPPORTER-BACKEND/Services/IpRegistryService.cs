using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class IpRegistryService
{
    private const string FileName = "ip_registry.json";
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<IpMapping> _entries = [];
    private readonly ILogger<IpRegistryService>? _logger;

    public IpRegistryService(ILogger<IpRegistryService>? logger = null)
    {
        _logger = logger;
        _filePath = ResolvePath(FileName);
        Load();
    }

    public string? Lookup(string ip)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e =>
                string.Equals(e.Ip, ip, StringComparison.OrdinalIgnoreCase))?.Name;
        }
    }

    public IpMapping? GetByName(string name)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e =>
                string.Equals(e.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public string? ResolveToIp(string nameOrIp)
    {
        var value = nameOrIp?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        if (IPAddress.TryParse(value, out _))
            return value;

        return GetByName(value)?.Ip;
    }

    public void Register(string ip, string name, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[IPREG] Skip Register with empty values: ip='{Ip}', name='{Name}'", ip, name);
            return;
        }

        ip = ip.Trim();
        name = name.Trim();
        _logger?.LogInformation("[IPREG] Register: {Ip} → {Name}", ip, name);
        lock (_lock)
        {
            var existing = _entries.FirstOrDefault(e =>
                string.Equals(e.Ip, ip, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger?.LogInformation("[IPREG] Updating existing: {Ip} ({Name})", existing.Ip, existing.Name);
                _entries.Remove(existing);
            }

            _entries.Add(new IpMapping(ip, name, notes, DateTime.UtcNow));
            Save();
        }
    }

    public IReadOnlyList<IpMapping> GetAll()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _entries = [];
                Save();
                return;
            }

            var json = File.ReadAllText(_filePath);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("entries", out var entries))
            {
                _entries = JsonSerializer.Deserialize<List<IpMapping>>(entries.GetRawText()) ?? [];
            }

            var clean = _entries.Where(e =>
                !string.IsNullOrWhiteSpace(e.Ip) &&
                !string.IsNullOrWhiteSpace(e.Name) &&
                e.FirstSeen > DateTime.MinValue).ToList();

            if (clean.Count != _entries.Count)
            {
                _logger?.LogWarning("[IPREG] Load: removed {Count} invalid entries", _entries.Count - clean.Count);
                _entries = clean;
                Save();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[IPREG] Load error");
            _entries = [];
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(new { entries = _entries }, AppJson.File);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[IPREG] Save error");
        }
    }

    private static string ResolvePath(string fileName)
    {
        var dirs = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
        };

        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path)) return path;
        }

        return Path.Combine(Environment.CurrentDirectory, fileName);
    }
}
