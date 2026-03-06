# Extending Vitruvian

This guide is for plugin authors who want to add new capabilities to Vitruvian.

---

## Table of Contents

- [Create a Plugin Project](#1-create-a-plugin-project)
- [Implement IVitruvianModule](#2-implement-ivitruvianmodule)
- [Add SDK Attributes](#3-add-sdk-attributes)
- [Declare Permissions](#4-declare-permissions)
- [Declare Required API Keys](#5-declare-required-api-keys)
- [Using Tools with IModelClient](#6-using-tools-with-imodelclient)
- [Build and Install](#7-build-and-install)
- [Module Best Practices](#8-module-best-practices)

---

## 1. Create a Plugin Project

1. Create a `net8.0` class library.
2. Add a project reference to `Vitruvian.Abstractions` (for `IVitruvianModule`) and optionally `Vitruvian.PluginSdk` (for SDK attributes).
3. Implement `IVitruvianModule`.

---

## 2. Implement `IVitruvianModule`

Every Vitruvian capability implements the same interface:

```csharp
using VitruvianAbstractions.Interfaces;

public sealed class TranslationModule : IVitruvianModule
{
    private readonly IModelClient? _modelClient;

    public string Domain => "translation";
    public string Description => "Translate text between languages using AI";

    public TranslationModule(IModelClient? modelClient = null)
        => _modelClient = modelClient;

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null) return "No model configured for translation.";
        return await _modelClient.GenerateAsync(
            $"Translate the following as requested: {request}", ct);
    }
}
```

| Property | Purpose |
|----------|---------|
| `Domain` | Unique identifier for this module (e.g. `"translation"`). Used by the planner to assign steps. |
| `Description` | Natural language description shown to the LLM so it can reason about when to use this module. **Be specific.** |

---

## 3. Add SDK Attributes

Use SDK attributes from `VitruvianPluginSdk.Attributes` so the governance pipeline can route and score proposals:

```csharp
using VitruvianPluginSdk.Attributes;

[VitruvianCapability("translation", priority: 5)]
[VitruvianGoals(GoalTag.Answer)]
[VitruvianLane(Lane.Execute)]
[VitruvianCost(0.1)]
[VitruvianRisk(0.0)]
[VitruvianCooldown("translation.translate", secondsTtl: 30)]
public sealed class TranslationModule : IVitruvianModule { /* ... */ }
```

| Attribute | Purpose |
|-----------|---------|
| `[VitruvianCapability]` | Domain name and priority |
| `[VitruvianGoals]` | Which `GoalTag` values this module serves (Answer, Clarify, Summarize, Execute, …) |
| `[VitruvianLane]` | Which `Lane` this module belongs to (Interpret, Plan, Execute, Communicate, …) |
| `[VitruvianCost]` | Cost factor (0.0–1.0) subtracted from the effective score |
| `[VitruvianRisk]` | Risk factor (0.0–1.0) subtracted from the effective score |
| `[VitruvianCooldown]` | Cooldown period preventing repeated invocation |
| `[VitruvianConflicts]` | Conflict IDs/tags to prevent concurrent selection with other modules |

---

## 4. Declare Permissions

Modules that access files, write state, or execute processes must declare their required permissions. The runtime enforces these declarations before execution — undeclared access is denied.

```csharp
using VitruvianPluginSdk.Attributes;

[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Write, resource: "files/*")]
public sealed class MyFileModule : IVitruvianModule
{
    // ...
}
```

Available access levels (combinable as flags):

| Flag | Description |
|------|-------------|
| `ModuleAccess.Read` | Read files or resources |
| `ModuleAccess.Write` | Create or modify files or resources |
| `ModuleAccess.Execute` | Run commands or spawn processes |

See [Security — Permission Model](SECURITY.md#permission-model) for the full enforcement model.

---

## 5. Declare Required API Keys

If your module needs API keys or other secrets at runtime, declare them with the `[RequiresApiKey]` attribute. Each attribute takes the name of the environment variable that must hold the key:

```csharp
using VitruvianPluginSdk.Attributes;

[RequiresApiKey("WEATHER_API_KEY")]
[RequiresApiKey("GEOCODING_API_KEY")]
public sealed class WeatherModule : IVitruvianModule
{
    public string Domain => "weather";
    public string Description => "Fetches weather data";

    public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("WEATHER_API_KEY");
        // ... use apiKey to call the external service ...
        return Task.FromResult("sunny");
    }
}
```

### How it works

| Phase | What happens |
|-------|--------------|
| **Install time** | The installer scans the module DLL for `[RequiresApiKey]` attributes, checks whether each environment variable is already set, and prompts the user for any missing values. Provided values are persisted to `.env.Vitruvian`. |
| **Startup** | `EnvFileLoader` loads `.env.Vitruvian` before any plugin code runs, making the keys available via `Environment.GetEnvironmentVariable()`. |
| **Module load** | Missing API keys emit `[WARN]` messages. The module is still loaded but may fail at execution time. |

### Providing keys manually

```bash
# Option 1 — add to .env.Vitruvian
echo 'WEATHER_API_KEY=sk-abc123' >> .env.Vitruvian

# Option 2 — export before running
export WEATHER_API_KEY=sk-abc123
dotnet run --project src/Vitruvian.Cli
```

You may additionally list the same keys in your plugin's `vitruvian-manifest.json` under `RequiredSecrets`:

```json
{
  "RequiredSecrets": ["WEATHER_API_KEY", "GEOCODING_API_KEY"]
}
```

---

## 6. Using Tools with IModelClient

Modules can give the AI model access to tools (functions) that it can call during generation. Vitruvian provides a fluent API for defining tools and managing the tool execution loop.

### Defining Tools

Use `ModelToolBuilder` for fluent tool definitions instead of manual dictionary construction:

```csharp
using VitruvianAbstractions.Interfaces;

// Simple tool with no parameters
var webSearch = new ModelToolBuilder("web_search", "Search the web").Build();

// Tool with typed parameters
var translateTool = new ModelToolBuilder("translate", "Translate text between languages")
    .AddParameter("text", "The text to translate")
    .AddParameter("targetLanguage", "Target language code (e.g. 'en', 'da', 'fr')")
    .Build();
```

You can also define tools as static fields for reuse across requests:

```csharp
public sealed class MyModule : IVitruvianModule
{
    private static readonly ModelTool LookupTool = new ModelToolBuilder("lookup_user", "Look up a user by email")
        .AddParameter("email", "The user's email address")
        .Build();

    private static readonly ModelTool SendNotificationTool = new ModelToolBuilder("send_notification", "Send a notification")
        .AddParameter("userId", "Target user ID")
        .AddParameter("message", "Notification message text")
        .Build();

    // ...
}
```

### Simple Tool Usage

Pass tools to `CompleteAsync` for the model to use. The model decides whether to call a tool based on the request:

```csharp
var response = await _modelClient.CompleteAsync(
    systemMessage: "You are a translator. Use the translate tool when needed.",
    userMessage: "Translate 'hello world' to Danish",
    tools: [translateTool],
    cancellationToken: ct);
```

### Getting Tool Call Information

Use `CompleteWithToolInfoAsync` when you need to inspect which tool the model called:

```csharp
var response = await _modelClient.CompleteWithToolInfoAsync(
    systemMessage: "You are a helpful assistant.",
    userMessage: "Look up user john@example.com",
    tools: [LookupTool]);

if (response.ToolCall is not null)
{
    // Model wants to call a tool — handle it
    Console.WriteLine($"Tool: {response.ToolCall}, Args: {response.ToolArguments}");
}
else
{
    // Model returned a direct text response
    Console.WriteLine(response.Text);
}
```

### Automatic Tool Execution Loop

`ExecuteWithToolsAsync` handles the full tool loop: send request → model calls tool → handler returns result → feed back to model → repeat until final text:

```csharp
public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
{
    if (_modelClient is null) return "No model configured.";

    return await _modelClient.ExecuteWithToolsAsync(
        systemMessage: "You are a helpful assistant with lookup and notification tools.",
        userMessage: request,
        tools: [LookupTool, SendNotificationTool],
        toolHandler: async (toolName, toolArgs, innerCt) =>
        {
            return toolName switch
            {
                "lookup_user" => await LookupUser(toolArgs),
                "send_notification" => await SendNotification(toolArgs),
                _ => $"Unknown tool: {toolName}"
            };
        },
        cancellationToken: ct);
}
```

Or use the dictionary overload for named routing:

```csharp
var handlers = new Dictionary<string, Func<string?, CancellationToken, Task<string>>>
{
    ["lookup_user"] = async (args, ct) => await LookupUser(args),
    ["send_notification"] = async (args, ct) => await SendNotification(args)
};

var result = await _modelClient.ExecuteWithToolsAsync(
    "You are a helpful assistant.", request,
    [LookupTool, SendNotificationTool], handlers,
    cancellationToken: ct);
```

### MCP Tools

Vitruvian supports [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) for connecting to external tool servers. MCP tools are detected automatically when parameters contain `server_url` or `connector_id`:

```csharp
// Remote MCP server
var fileServer = new ModelToolBuilder("filesystem", "Access project files")
    .AddParameter("server_url", "https://mcp.example.com/filesystem")
    .AddParameter("server_label", "project-files")
    .AddParameter("require_approval", "always")
    .Build();

// OpenAI connector-based MCP tool
var slackTool = new ModelToolBuilder("slack", "Team Slack workspace")
    .AddParameter("connector_id", "conn_abc123")
    .AddParameter("server_label", "team-slack")
    .AddParameter("server_description", "Post messages and read channels")
    .Build();

// MCP tool with auth and allowed tool filtering
var githubTool = new ModelToolBuilder("github", "GitHub repository operations")
    .AddParameter("server_url", "https://mcp.example.com/github")
    .AddParameter("server_label", "github-repos")
    .AddParameter("authorization", "Bearer ghp_xxxx")
    .AddParameter("require_approval", "never")
    .AddParameter("allowed_tools", "list_repos,get_file,search_code")
    .Build();
```

MCP tools are forwarded natively to providers that support them (OpenAI, Anthropic). When the provider returns `mcp_approval_request`, Vitruvian routes approval through the configured HITL gate automatically.

| MCP Parameter | Required | Description |
|---------------|----------|-------------|
| `server_url` | One of `server_url`/`connector_id` | Remote MCP server URL |
| `connector_id` | One of `server_url`/`connector_id` | OpenAI connector ID |
| `server_label` | No | Display label (defaults to tool name) |
| `server_description` | No | Server description |
| `authorization` | No | Auth header (e.g. `Bearer token`) |
| `require_approval` | No | `"always"`, `"never"`, or JSON config |
| `allowed_tools` | No | CSV or JSON array of allowed tool names |

### Complete Module Example with Tools and MCP

```csharp
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

[RequiresPermission(ModuleAccess.Read)]
[RequiresApiKey("GITHUB_TOKEN")]
public sealed class CodeReviewModule : IVitruvianModule
{
    private readonly IModelClient? _modelClient;

    // MCP tool — handled by the provider natively
    private static readonly ModelTool GitHubMcp = new ModelToolBuilder("github", "GitHub operations")
        .AddParameter("server_url", "https://mcp.example.com/github")
        .AddParameter("server_label", "github")
        .AddParameter("require_approval", "always")
        .AddParameter("allowed_tools", "get_pull_request,list_files,get_file_content")
        .Build();

    // Function tool — handled by your code
    private static readonly ModelTool AnalyzeTool = new ModelToolBuilder("analyze_code", "Analyze code quality")
        .AddParameter("code", "Source code to analyze")
        .AddParameter("language", "Programming language")
        .Build();

    public string Domain => "code-review";
    public string Description => "Review pull requests using GitHub and code analysis tools";

    public CodeReviewModule(IModelClient? modelClient = null) => _modelClient = modelClient;

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null) return "No model configured.";

        // MCP tools (github) are handled natively by the provider.
        // Function tools (analyze_code) are routed to the toolHandler callback.
        return await _modelClient.ExecuteWithToolsAsync(
            systemMessage: "You are a code reviewer. Use GitHub to fetch PR details and analyze code quality.",
            userMessage: request,
            tools: [GitHubMcp, AnalyzeTool],
            toolHandler: async (toolName, args, innerCt) =>
            {
                if (toolName == "analyze_code")
                    return AnalyzeCode(args);
                return $"Unknown tool: {toolName}";
            },
            cancellationToken: ct);
    }

    private static string AnalyzeCode(string? args) => "Analysis complete: no issues found.";
}
```

---

## 7. Build and Install

Build/publish the plugin and place outputs in a `plugins/` folder next to the host executable, or install via CLI:

```bash
# Build
dotnet publish -c Release

# Copy to plugins directory
cp bin/Release/net8.0/publish/* \
   path/to/Vitruvian.Cli/bin/Debug/net8.0/plugins/

# Or install interactively
# In the Vitruvian CLI:
> /install-module /absolute/path/MyPlugin.dll
```

---

## 8. Module Best Practices

| Practice | Why |
|----------|-----|
| **Write a clear `Description`** | The GOAP planner and LLM router use this to decide when to invoke your module. Be specific: *"Translate text between languages using AI"* not *"Does stuff"*. |
| **Accept `IModelClient?` as optional** | Allows your module to work in environments without an LLM (graceful degradation). |
| **Return user-friendly error messages** | Errors bubble up as plan step results. Clear messages help users understand what happened. |
| **Use `sealed`** | Mark your module class as `sealed` unless inheritance is intentional. |
| **Keep `ExecuteAsync` focused** | One responsibility per module. The GOAP planner handles orchestration across modules. |
| **Declare permissions** | Use `[RequiresPermission]` to declare what access your module needs. The runtime enforces this before execution. |
| **Declare external secrets** | Use `[RequiresApiKey("MY_API_KEY")]` (repeatable) so the installer can prompt for missing keys at install time. |
| **Use `ICommandRunner` for process execution** | For modules that run commands, depend on `ICommandRunner` and use the shared `ProcessCommandRunner` implementation instead of creating your own `Process` logic. |

For command-executing modules, inject and use the shared command runner abstraction:

```csharp
using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;

public sealed class MyCommandModule : IVitruvianModule
{
    private readonly ICommandRunner _commandRunner;

    public MyCommandModule(ICommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new ProcessCommandRunner();
    }
}
```
