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
            "they would like to start a consultation before proceeding.\n\n" +
            "IMPORTANT — choosing between classify and start_consultation:\n" +
            "- If the user's message already contains values for ALL attributes of the knowledge base, " +
            "use classify directly — do NOT start a consultation.\n" +
            "- If some attributes are missing or unclear, use start_consultation. Then, for each question " +
            "the tree asks, if the answer is already known from the user's message, call answer_question " +
            "immediately without asking the user. Only ask the user for attributes you genuinely don't know.\n" +
            "- Use get_attributes to check what inputs a knowledge base expects if you are unsure whether " +
            "you have all of them.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
