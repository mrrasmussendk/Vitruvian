using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Summarization module implementing ICompassModule.
/// Summarizes text, conversations, or documents into concise overviews.
/// </summary>
public sealed class SummarizationModule : ICompassModule
{
    private readonly IModelClient? _modelClient;

    public string Domain => "summarization";
    public string Description => "Summarize text, conversations, or documents into concise overviews";

    public SummarizationModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null)
            return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var systemMessage = "You are a summarization assistant. Provide a clear and concise summary of the content the user provides, including key points.";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   SimpleSummarization LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
