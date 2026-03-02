using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Web search module implementing ICompassModule.
/// Performs web searches by leveraging the model client's web search capabilities.
/// </summary>
public sealed class WebSearchModule : ICompassModule
{
    private readonly IModelClient? _modelClient;

    /// <summary>Web search tool declared for the model request.</summary>
    public static readonly ModelTool WebSearchTool = new(
        "web_search",
        "Search the web for real-time information",
        new Dictionary<string, string> { ["query"] = "string" });

    public string Domain => "web-search";
    public string Description => "Search the web for current information, weather forecasts, news, recent events, and real-time data";

    public WebSearchModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null)
            return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var systemMessage = "You are a web search assistant. Provide a concise, factual answer to the user's query with sources.";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            tools: [WebSearchTool],
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   SimpleWebSearch LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
