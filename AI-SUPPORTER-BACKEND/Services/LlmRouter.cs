using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AI_Supporter;

public sealed class LlmRouter
{
    private readonly LlmClient _localClient;
    private readonly LlmClient _apiClient;
    private readonly UserStore _users;
    private readonly LlmSettings _settings;
    private readonly ILogger<LlmRouter>? _logger;

    private readonly ConcurrentDictionary<string, (DateTime Time, CapabilitiesDto Caps)> _capsCache = new();

    public LlmRouter(
        LlmClient localClient,
        LlmClient apiClient,
        UserStore users,
        LlmSettings settings,
        ILogger<LlmRouter>? logger = null)
    {
        _localClient = localClient;
        _apiClient = apiClient;
        _users = users;
        _settings = settings;
        _logger = logger;
    }

    public async Task<(LlmClient Client, LlmRequestOptions Options, string Model)> ResolveAsync(LlmTask task, string username)
    {
        var profile = _users.GetLlmProfile(username);
        var selection = task switch
        {
            LlmTask.Chat => profile.Chat,
            LlmTask.Voice => profile.Voice,
            LlmTask.Vision => profile.Vision,
            LlmTask.ToolCalling => profile.ToolCalling,
            LlmTask.FastToolCalling => profile.ToolCalling,
            _ => profile.Chat
        };

        // For FastToolCalling, use lower temp
        var isFast = task == LlmTask.FastToolCalling;

        (LlmClient Client, string Provider, LlmRequestOptions Options, string Model) resolved;
        if (selection.AutoSelect)
        {
            resolved = await AutoSelectBestAsync(task, selection.Provider, selection.Model, isFast);
        }
        else
        {
            var provider = selection.Provider == "api" ? "api" : "local";
            if (provider == "api" && string.IsNullOrWhiteSpace(_settings.Api.Key))
            {
                provider = "local";
                _logger?.LogWarning("[LLM] Профиль просит api, но ключ не задан — фолбэк на локальную модель");
            }
            var model = string.IsNullOrEmpty(selection.Model)
                ? GetDefaultModel(provider)
                : selection.Model;

            var client = provider == "api" ? _apiClient : _localClient;
            var options = GetOptions(task, isFast);

            resolved = (client, provider, options, model);
        }

        var routedOptions = resolved.Options with
        {
            Model = resolved.Model,
            UseApi = resolved.Provider == "api"
        };

        return (resolved.Client, routedOptions, resolved.Model);
    }

    private async Task<(LlmClient Client, string Provider, LlmRequestOptions Options, string Model)> AutoSelectBestAsync(LlmTask task, string preferredProvider, string preferredModel, bool isFast)
    {
        // Check capabilities cache
        var caps = await GetCapabilitiesAsync();
        var apiKeyProvided = !string.IsNullOrWhiteSpace(_settings.Api.Key);

        // Vision → prefer API (better vision models), fallback to local if API not available
        if (task == LlmTask.Vision)
        {
            if (apiKeyProvided && caps.Api?.Vision == true && (preferredProvider == "api" || preferredProvider == "auto"))
            {
                return (_apiClient, "api", GetOptions(task, isFast), PickModel(caps.Api?.Models, preferredModel, _settings.Api.Model, visionOnly: true, isBlocked: _apiClient.IsApiModelBlocked));
            }
            if (caps.Local?.Vision == true)
            {
                return (_localClient, "local", GetOptions(task, isFast), PickModel(caps.Local?.Models, preferredModel, _settings.EffectiveLocalModel, visionOnly: true));
            }
            if (apiKeyProvided && caps.Api?.Vision == true)
            {
                return (_apiClient, "api", GetOptions(task, isFast), PickModel(caps.Api?.Models, preferredModel, _settings.Api.Model, visionOnly: true, isBlocked: _apiClient.IsApiModelBlocked));
            }
            // Fallback to local when API key is not set
            return (_localClient, "local", GetOptions(task, isFast), _settings.EffectiveLocalModel);
        }

        // ToolCalling / FastToolCalling → prefer Local (faster, free)
        if (task == LlmTask.ToolCalling || task == LlmTask.FastToolCalling)
        {
            if (caps.Local?.Tools == true)
            {
                return (_localClient, "local", GetOptions(task, isFast), PickModel(caps.Local?.Models, preferredModel, _settings.EffectiveLocalModel));
            }
            if (caps.Api?.Tools == true)
            {
                return (_apiClient, "api", GetOptions(task, isFast), PickModel(caps.Api?.Models, preferredModel, _settings.Api.Model, isBlocked: _apiClient.IsApiModelBlocked));
            }
        }

        // Voice → prefer Local (lower latency)
        if (task == LlmTask.Voice)
        {
            if (caps.Local?.Chat == true)
            {
                return (_localClient, "local", GetOptions(task, isFast), PickModel(caps.Local?.Models, preferredModel, _settings.EffectiveLocalModel));
            }
            if (caps.Api?.Chat == true)
            {
                return (_apiClient, "api", GetOptions(task, isFast), PickModel(caps.Api?.Models, preferredModel, _settings.Api.Model, isBlocked: _apiClient.IsApiModelBlocked));
            }
        }

        // Chat → use preferred provider (auto = like api when API-key is present)
        if ((preferredProvider is "api" or "auto") && apiKeyProvided && caps.Api?.Chat == true)
        {
            return (_apiClient, "api", GetOptions(task, isFast), PickModel(caps.Api?.Models, preferredModel, _settings.Api.Model, isBlocked: _apiClient.IsApiModelBlocked));
        }
        if (caps.Local?.Chat == true)
        {
            return (_localClient, "local", GetOptions(task, isFast), PickModel(caps.Local?.Models, preferredModel, _settings.EffectiveLocalModel));
        }
        if (caps.Api?.Chat == true)
        {
            return (_apiClient, "api", GetOptions(task, isFast), PickModel(caps.Api?.Models, preferredModel, _settings.Api.Model, isBlocked: _apiClient.IsApiModelBlocked));
        }

        // Ultimate fallback
        return (_localClient, "local", GetOptions(task, isFast), _settings.EffectiveLocalModel);
    }

    private static string PickModel(List<ModelCapabilityDto>? models, string preferred, string fallback, bool visionOnly = false, Func<string, bool>? isBlocked = null)
    {
        var pool = visionOnly ? models?.Where(m => m.Vision).ToList() : models;
        if (isBlocked is not null)
            pool = pool?.Where(m => !isBlocked(m.Id)).ToList();
        if (!string.IsNullOrEmpty(preferred) && pool?.Any(m => m.Id == preferred) == true)
            return preferred;
        if (!string.IsNullOrEmpty(preferred) && models is not { Count: > 0 })
            return preferred;
        if (!string.IsNullOrEmpty(fallback) && pool?.Any(m => m.Id == fallback) == true)
            return fallback;
        return pool?.FirstOrDefault()?.Id ?? fallback;
    }

    private async Task<CapabilitiesDto> GetCapabilitiesAsync()
    {
        if (_capsCache.TryGetValue("all", out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5))
            return cached.Caps;

        var caps = new CapabilitiesDto
        {
            Local = await CheckLocalCapabilities(),
            Api = await CheckApiCapabilitiesAsync()
        };

        _capsCache["all"] = (DateTime.UtcNow, caps);
        return caps;
    }

    private async Task<ProviderCapabilitiesDto?> CheckLocalCapabilities()
    {
        try
        {
            var models = await _localClient.GetAvailableLocalModelsAsync();
            if (models == null || models.Count == 0) return null;

            var visionModels = models.Where(m => m.Contains("llava", StringComparison.OrdinalIgnoreCase)
                                              || m.Contains("bakllava", StringComparison.OrdinalIgnoreCase)
                                              || m.Contains("moondream", StringComparison.OrdinalIgnoreCase)
                                              || m.Contains("vision", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

            return new ProviderCapabilitiesDto
            {
                Available = true,
                Chat = true,
                Tools = true,
                Vision = visionModels.Count > 0,
                Models = models.Select(m => new ModelCapabilityDto
                {
                    Id = m,
                    Vision = visionModels.Contains(m),
                    Tasks = visionModels.Contains(m) ? new[] { "vision" } : new[] { "chat", "voice", "toolCalling" }
                }).ToList()
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<ProviderCapabilitiesDto?> CheckApiCapabilitiesAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.Api.Key)) return null;

        var models = await _apiClient.GetAvailableApiModelsAsync();
        if (models is { Count: > 0 })
        {
            return new ProviderCapabilitiesDto
            {
                Available = true,
                Chat = true,
                Tools = true,
                Vision = models.Any(IsApiVisionModel),
                Models = models.Select(m => new ModelCapabilityDto
                {
                    Id = m,
                    Vision = IsApiVisionModel(m),
                    Tasks = IsApiVisionModel(m) ? new[] { "vision" } : new[] { "chat", "voice", "toolCalling" }
                }).ToList()
            };
        }

        return null;
    }

    private static bool IsApiVisionModel(string id)
    {
        return id.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase)
            || id.Contains("gpt-5", StringComparison.OrdinalIgnoreCase)
            || id.Contains("gemini", StringComparison.OrdinalIgnoreCase)
            || id.Contains("claude", StringComparison.OrdinalIgnoreCase)
            || id.Contains("vision", StringComparison.OrdinalIgnoreCase)
            || id.Contains("vl", StringComparison.OrdinalIgnoreCase)
            || id.Contains("llava", StringComparison.OrdinalIgnoreCase)
            || id.Contains("moondream", StringComparison.OrdinalIgnoreCase)
            || id.Contains("glm", StringComparison.OrdinalIgnoreCase)
            || id.Contains("pixtral", StringComparison.OrdinalIgnoreCase)
            || id.Contains("reka", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CapabilitiesDto> GetCapabilitiesDtoAsync(bool refresh = false)
    {
        if (refresh)
            _capsCache.Clear();
        return await GetCapabilitiesAsync();
    }

    public async Task<(string Provider, string Model, bool Ok)> ResolveBriefAsync(string task, string username)
    {
        var llmTask = task switch
        {
            "voice" => LlmTask.Voice,
            "vision" => LlmTask.Vision,
            "toolCalling" => LlmTask.ToolCalling,
            "fastToolCalling" => LlmTask.FastToolCalling,
            _ => LlmTask.Chat
        };

        var (_, options, model) = await ResolveAsync(llmTask, username);
        var provider = options.UseApi == true ? "api" : "local";
        var caps = await GetCapabilitiesAsync();
        var ok = provider == "api" ? (caps.Api?.Available ?? false) : (caps.Local?.Available ?? false);
        return (provider, model, ok);
    }

    public void InvalidateCapabilities()
    {
        _capsCache.Clear();
        _apiClient.ClearBlockedApiModels();
    }

    private static LlmRequestOptions GetOptions(LlmTask task, bool isFast)
    {
        return task switch
        {
            LlmTask.Chat => new LlmRequestOptions(Temperature: 0.7, NumPredict: 512),
            LlmTask.Voice => new LlmRequestOptions(Temperature: 0.3, NumPredict: 256),
            LlmTask.Vision => new LlmRequestOptions(Temperature: 0.3, NumPredict: 512),
            LlmTask.ToolCalling => new LlmRequestOptions(Temperature: 0.0, NumPredict: 256, Format: "json"),
            LlmTask.FastToolCalling => new LlmRequestOptions(Temperature: 0.0, NumPredict: 128, Format: "json"),
            _ => new LlmRequestOptions(Temperature: 0.7, NumPredict: 512)
        };
    }

    private string GetDefaultModel(string provider)
    {
        return provider == "api" ? _settings.Api.Model : _settings.EffectiveLocalModel;
    }
}

// DTOs for capabilities
public sealed class CapabilitiesDto
{
    public ProviderCapabilitiesDto? Local { get; set; }
    public ProviderCapabilitiesDto? Api { get; set; }
}

public sealed class ProviderCapabilitiesDto
{
    public bool Available { get; set; }
    public bool Chat { get; set; }
    public bool Tools { get; set; }
    public bool Vision { get; set; }
    public List<ModelCapabilityDto> Models { get; set; } = new();
}

public sealed class ModelCapabilityDto
{
    public string Id { get; set; } = "";
    public bool Vision { get; set; }
    public string[] Tasks { get; set; } = Array.Empty<string>();
}