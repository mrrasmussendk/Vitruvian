using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Conversation module implementing ICompassModule.
/// Handles general conversational requests using conversational AI.
/// Acts as a fallback when no specialized module matches.
/// </summary>
public sealed class ConversationModule : ICompassModule
{
    private readonly IModelClient? _modelClient;

    public string Domain => "conversation";
    public string Description => "Answer general questions, provide explanations, and have conversations (fallback for queries not handled by specialized modules)";

    public ConversationModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null)
            return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var systemMessage = "You are a helpful conversational AI assistant.";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   Conversation LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
