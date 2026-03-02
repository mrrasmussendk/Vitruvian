using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Gmail module implementing ICompassModule.
/// Reads Gmail messages and creates draft replies (does not send directly).
/// </summary>
public sealed class GmailModule : ICompassModule
{
    private readonly IModelClient? _modelClient;

    public static readonly ModelTool GmailReadTool = new(
        "gmail_read_messages",
        "Read messages from Gmail inbox",
        new Dictionary<string, string> { ["query"] = "string", ["maxResults"] = "number" });

    public static readonly ModelTool GmailDraftTool = new(
        "gmail_create_draft",
        "Create a Gmail draft reply (partial write, does not send)",
        new Dictionary<string, string> { ["to"] = "string", ["subject"] = "string", ["body"] = "string" });

    public static IReadOnlyList<string> RequiredGoogleScopes =>
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.compose"
    ];

    public string Domain => "gmail";
    public string Description => "Read and send Gmail emails";

    public GmailModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null)
            return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var systemMessage = "You are a Gmail assistant. You may read Gmail messages and create draft replies only. Never send messages directly.";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            tools: [GmailReadTool, GmailDraftTool],
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   SimpleGmail LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
