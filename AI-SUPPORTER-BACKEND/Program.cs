using AI_Supporter;
using Microsoft.Extensions.DependencyInjection;

Console.Title = "AI Supporter — Backend";

// Build DI container
var services = new ServiceCollection();
ServiceRegistrar.Configure(services);
var provider = services.BuildServiceProvider();

await ConsoleMenu.RunBackendAsync(provider);
