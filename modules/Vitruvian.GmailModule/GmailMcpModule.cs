using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

namespace VitruvianGmailModule;

/// <summary>
/// Gmail MCP module implementing IVitruvianModule.
/// Connects to a remote Gmail MCP server to read, search, and draft emails.
/// Requires a <c>GOOGLE_API_TOKEN</c> environment variable for authentication.
/// </summary>
[RequiresPermission(ModuleAccess.Read)]
[RequiresApiKey("GOOGLE_API_TOKEN")]
public sealed class GmailMcpModule(IModelClient? modelClient = null) : IVitruvianModule
{
    /// <summary>
    /// MCP tool connecting to a Gmail MCP server for reading and searching messages.
    /// </summary>
    public static ModelTool GmailMcpTool;
    public string Domain => "gmail-mcp";
    public string Description => "Read, search, and draft Gmail messages using MCP";

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (modelClient is null)
            return "No model configured. Run 'Vitruvian --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

         GmailMcpTool = new ModelToolBuilder("gmail", "Gmail operations via MCP")
            .AddParameter("server_label", "gmail-mcp")
            .AddParameter("connector_id", "connector_gmail")
            .AddParameter("server_description", "Read, search, and draft Gmail messages")
            .AddParameter("require_approval", "never")
            .AddParameter("authorization", Environment.GetEnvironmentVariable("GOOGLE_API_TOKEN") ?? string.Empty)
            
            .Build();
        var systemMessage =
            "You are a Gmail assistant. Use the Gmail MCP server to read, search, and draft emails. " +
            "You may create draft replies but never send messages directly.";

        var response = await modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            tools: [GmailMcpTool],
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   GmailMcp LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
