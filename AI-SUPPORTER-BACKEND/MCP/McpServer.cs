using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace AI_Supporter;

public static class McpServer
{
    private static readonly string JwtIssuer = "ai-supporter";
    private static readonly string JwtAudience = "ai-supporter-client";
    private static readonly SymmetricSecurityKey JwtKey = LoadJwtKey();

    public static ToolRegistry? Registry { get; private set; }

    public static SseService? Sse { get; private set; }

    public static IReadOnlyList<object>? ReloadProviders { get; private set; }

    public static async Task StartAsync(
        ToolRegistry registry,
        WebHostServices? webServices = null,
        Func<string, Task>? onMessage = null,
        CancellationToken ct = default,
        Func<JsonElement, Task>? onCommand = null,
        Func<byte[], Task>? onBinary = null,
        SseService? existingSse = null,
        Func<Task>? onConnect = null,
        IReadOnlyList<object>? reloadProviders = null)
    {
        ct.Register(() => Console.WriteLine("[MCP] Shutdown requested"));
        var builder = WebApplication.CreateBuilder();

        var httpPort = Environment.GetEnvironmentVariable("AI_SUPPORTER_HTTP_PORT") ?? "5000";
        var urls = $"http://0.0.0.0:{httpPort}";
        if (bool.TryParse(Environment.GetEnvironmentVariable("AI_SUPPORTER_HTTPS"), out var useHttps) && useHttps)
        {
            var httpsPort = Environment.GetEnvironmentVariable("AI_SUPPORTER_HTTPS_PORT") ?? "5001";
            urls += $";https://0.0.0.0:{httpsPort}";
        }
        builder.WebHost.UseUrls(urls);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = JwtAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtKey
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["token"];
                        if (!string.IsNullOrEmpty(token))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();

        var tools = registry.GetAll().Select(d => new McpToolAdapter(d, registry)).ToList();

        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        var sse = existingSse ?? new SseService();
        builder.Services.AddSingleton(sse);

        builder.Logging.AddConsole();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        Registry = registry;
        Sse = sse;
        ReloadProviders = reloadProviders;

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            var sw = Stopwatch.StartNew();
            await next();
            sw.Stop();

            if (ctx.Response.StatusCode >= 400 || sw.ElapsedMilliseconds > 500)
            {
                var logger = ctx.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AI_Supporter.Http");
                logger?.LogInformation("{Method} {Path} {StatusCode} {Elapsed}ms",
                    ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        });

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (ctx, next) =>
        {
            RequestContext.Current.Value = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
            try { await next(); }
            finally { RequestContext.Current.Value = null; }
        });

        app.MapMcp("/mcp").RequireAuthorization();

        app.MapGet("/events", async (HttpContext ctx, SseService sse, CancellationToken c) =>
            await sse.HandleSseAsync(ctx, c)).RequireAuthorization();

        app.Map("/ws", async (HttpContext ctx, SseService sse, CancellationToken c) =>
            await sse.HandleWebSocketAsync(ctx, onMessage, c, onCommand: onCommand, onBinary: onBinary, onConnect: onConnect)).RequireAuthorization();

        if (webServices is not null)
            app.MapApiEndpoints(webServices);

        ct.Register(() => app.StopAsync());
        await app.RunAsync();
    }

    public static string GenerateToken(string username, string role = "user")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(JwtKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SymmetricSecurityKey LoadJwtKey()
    {
        var env = Environment.GetEnvironmentVariable("AI_SUPPORTER_JWT_KEY");
        if (!string.IsNullOrWhiteSpace(env))
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(env));

        try
        {
            var path = Path.Combine(AppPaths.ProjectDir, "jwt_key.txt");
            if (File.Exists(path))
            {
                var key = File.ReadAllText(path).Trim();
                if (key.Length >= 32)
                    return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            }

            var generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
            File.WriteAllText(path, generated);
            Console.WriteLine("[JWT] Сгенерирован новый ключ подписи и сохранён в jwt_key.txt");
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(generated));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[JWT] Не удалось сохранить ключ в файл: {ex.Message}");
            return new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48));
        }
    }
}
