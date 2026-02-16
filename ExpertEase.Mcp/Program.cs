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
        options.ServerInstructions = "ExpertEase is an expert system shell. " +
            "Always call list_knowledge_bases first to discover available expert domains " +
            "that may match the user's question. If a match is found, ask the user if " +
            "they would like to start a consultation before proceeding.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
