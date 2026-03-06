using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

namespace VitruvianGoogleDriveModule;

/// <summary>
/// Google Drive MCP module implementing IVitruvianModule.
/// Connects to a remote Google Drive MCP server to read, search, and manage files.
/// Requires a <c>GOOGLE_DRIVE_TOKEN</c> environment variable for authentication.
/// </summary>
[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Write)]
[RequiresApiKey("GOOGLE_DRIVE_TOKEN")]
public sealed class GoogleDriveMcpModule : IVitruvianModule
{
    private readonly IModelClient? _modelClient;

    /// <summary>
    /// MCP tool connecting to a Google Drive MCP server for managing files.
    /// </summary>
    public static ModelTool GoogleDriveMcpTool;
    public string Domain => "google-drive-mcp";
    public string Description => "Read, search, and manage Google Drive files using MCP";

    public GoogleDriveMcpModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null)
            return "No model configured. Run 'Vitruvian --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

        var sw = System.Diagnostics.Stopwatch.StartNew();

         GoogleDriveMcpTool = new ModelToolBuilder("google_drive", "Google Drive operations via MCP")
            .AddParameter("server_label", "google-drive-mcp")
            .AddParameter("connector_id", "connector_googledrive")
            .AddParameter("server_description", "Read, search, and manage Google Drive files")
            .AddParameter("require_approval", "never")
            .AddParameter("authorization", Environment.GetEnvironmentVariable("GOOGLE_DRIVE_TOKEN") ?? string.Empty)

            .Build();
        var systemMessage =
            "You are a Google Drive assistant. Use the Google Drive MCP server to read, search, upload, download, and manage files. " +
            "You can list files, search for files, upload new files, download files, update existing files, and delete files as requested by the user.";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemMessage,
            userMessage: request,
            tools: [GoogleDriveMcpTool],
            cancellationToken: ct);

        Console.WriteLine($"[PERF]   GoogleDriveMcp LLM call: {sw.ElapsedMilliseconds}ms");

        return response;
    }
}
