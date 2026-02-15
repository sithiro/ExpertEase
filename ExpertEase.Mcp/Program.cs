using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "ExpertEase",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
