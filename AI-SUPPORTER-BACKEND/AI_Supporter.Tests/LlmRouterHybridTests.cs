using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

public class LlmRouterHybridTests
{
    [Fact]
    public async Task LlmRouter_AutoSelect_PreferredModelAvailableLocally_ResolvesPreferredModel()
    {
        var dir = TestHelper.CreateTempDir();
        var server = new MockRouterServer(new Dictionary<string, string>
        {
            ["/api/tags"] = "{\"models\":[{\"name\":\"qwen2.5:7b\"},{\"name\":\"llama3.1:8b\"}]}"
        });
        var ollamaUrl = $"http://127.0.0.1:{server.Port}/api/generate";
        Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", ollamaUrl);
        try
        {
            AppPaths.ProjectDir = dir;
            var settings = new LlmSettings { Provider = "local" };
            settings.Local.Url = ollamaUrl;
            var client = new LlmClient(settings);
            var store = new UserStore();
            store.SaveLlmProfile("alice", new UserLlmProfile
            {
                Username = "alice",
                Chat = new TaskModelSelection { Provider = "local", Model = "qwen2.5:7b", AutoSelect = true }
            });
            var router = new LlmRouter(client, client, store, settings);

            var (_, options, model) = await router.ResolveAsync(LlmTask.Chat, "alice");

            options.UseApi.Should().BeFalse();
            model.Should().Be("qwen2.5:7b");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", null);
            AppPaths.Reset();
            TestHelper.CleanupTempDir(dir);
            server.Dispose();
        }
    }

    [Fact]
    public async Task LlmRouter_AutoSelect_PreferredModelMissingFromApi_ResolvesFirstAvailableApiModel()
    {
        var dir = TestHelper.CreateTempDir();
        var server = new MockRouterServer(new Dictionary<string, string>
        {
            ["/openai/v1/models"] = "{\"data\":[{\"id\":\"gpt-5-mini\"},{\"id\":\"gemini-2.0-flash\"}]}"
        });
        var ollamaUrl = $"http://127.0.0.1:{server.Port}/api/generate";
        Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", ollamaUrl);
        try
        {
            AppPaths.ProjectDir = dir;
            var settings = new LlmSettings { Provider = "api" };
            settings.Api.Key = "test-key";
            settings.Api.BaseUrl = $"http://127.0.0.1:{server.Port}/openai/v1";
            settings.Local.Url = ollamaUrl;
            var client = new LlmClient(settings);
            var store = new UserStore();
            store.SaveLlmProfile("alice", new UserLlmProfile
            {
                Username = "alice",
                Chat = new TaskModelSelection { Provider = "api", Model = "fake-absent", AutoSelect = true }
            });
            var router = new LlmRouter(client, client, store, settings);

            var (_, options, model) = await router.ResolveAsync(LlmTask.Chat, "alice");

            options.UseApi.Should().BeTrue();
            model.Should().Be("gpt-5-mini");
            model.Should().NotBe("fake-absent");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_SUPPORTER_OLLAMA_URL", null);
            AppPaths.Reset();
            TestHelper.CleanupTempDir(dir);
            server.Dispose();
        }
    }

    private sealed class MockRouterServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<string, string> _responses;
        private readonly Task _loop;

        public MockRouterServer(Dictionary<string, string> responses)
        {
            _responses = responses;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(ServeAsync);
        }

        public int Port { get; }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try { _loop.GetAwaiter().GetResult(); } catch { }
        }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { break; }
                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var buffer = new byte[4096];
                var total = 0;
                while (total < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(total), _cts.Token);
                    if (read <= 0) break;
                    total += read;
                    if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
                }
                var requestText = Encoding.ASCII.GetString(buffer, 0, total);
                var firstLine = requestText.Split("\r\n", StringSplitOptions.None)[0];
                var path = firstLine.Split(' ').ElementAtOrDefault(1) ?? "/";

                var body = _responses.TryGetValue(path, out var content) ? content : "{}";
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                var responseBytes = Encoding.ASCII.GetBytes(header).Concat(bodyBytes).ToArray();
                await stream.WriteAsync(responseBytes, _cts.Token);
                await stream.FlushAsync(_cts.Token);
            }
        }
    }
}