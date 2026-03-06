using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

namespace VitruvianGoogleCalendarModule;

/// <summary>
/// Google Calendar MCP module implementing IVitruvianModule.
/// Connects to a remote Google Calendar MCP server to read, create, and manage calendar events.
/// Requires a <c>GOOGLE_API_TOKEN</c> environment variable for authentication.
/// </summary>
[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Execute)]
[RequiresApiKey("GOOGLE_API_TOKEN")]
public sealed class GoogleCalendarMcpModule(IModelClient? modelClient = null) : IVitruvianModule
{
    /// <summary>
    /// MCP tool connecting to a Google Calendar MCP server for managing calendar events.
    /// </summary>
    public static ModelTool GoogleCalendarMcpTool;
    public string Domain => "google-calendar-mcp";
    public string Description => "Read, create, and manage Google Calendar events using MCP";

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (modelClient is null)
            return "No model configured. Run 'Vitruvian --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

         GoogleCalendarMcpTool = new ModelToolBuilder("google_calendar", "Google Calendar operations via MCP")
            .AddParameter("server_label", "google-calendar-mcp")
            .AddParameter("connector_id", "connector_googlecalendar")
            .AddParameter("server_description", "Read, create, and manage Google Calendar events")
            .AddParameter("require_approval", "never")
            .AddParameter("authorization", Environment.GetEnvironmentVariable("GOOGLE_API_TOKEN") ?? string.Empty)

            .Build();
        var systemMessage =
            "You are a Google Calendar assistant. Use the Google Calendar MCP server to read, create, update, and manage calendar events. " +
            "You can list events, create new events, update existing events, and delete events as requested by the user.";

        var response = await modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            tools: [GoogleCalendarMcpTool],
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   GoogleCalendarMcp LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
