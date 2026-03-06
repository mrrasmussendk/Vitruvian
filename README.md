<div align="center">
  <img src="docs/logo.svg" alt="Vitruvian Logo" width="200"/>
</div>

# Vitruvian — .NET GOAP Agent Runtime

**.NET GOAP agent runtime** that decomposes user requests into dependency-aware execution plans, runs independent steps in parallel, and enforces human-in-the-loop approval, caching, and memory — all before any side-effecting action fires.

> **Vitruvian Agent Runtime** — modular, GOAP-driven AI agent orchestration for .NET.

Third-party modules plug in via a single interface (`IVitruvianModule`). The host handles planning, routing, governance, and security so module authors focus only on capability logic.

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download) (8.0.100 or later)
- Git
- An API key for OpenAI, Anthropic, or Google Gemini

### Clone, build, and test

```bash
git clone https://github.com/mrrasmussendk/Vitruvian.git
cd Vitruvian
dotnet build Vitruvian.sln
dotnet test Vitruvian.sln
```

### Configure a model provider

Create a `.env.Vitruvian` file in the project root (loaded automatically at startup):

```bash
VITRUVIAN_MODEL_PROVIDER=OpenAI
VITRUVIAN_OPENAI_API_KEY=sk-...
```

Or use the guided installer — see [docs/INSTALL.md](docs/INSTALL.md) for all options.

### Run the CLI

```bash
dotnet run --project src/Vitruvian.Cli
```

```
Vitruvian CLI started. Type a request (or 'quit' to exit):
>
```

Try some requests:

```
> What is the weather tomorrow?
> Create a file called notes.txt with content "Hello World"
> Read notes.txt then summarize it
```

---

## Features

| Feature | Description |
|---------|-------------|
| **GOAP Planning** | Decomposes requests into dependency-aware plans *before* execution. Multi-step tasks are broken into steps that run in parallel when independent. |
| **Multithreaded Execution** | Independent plan steps execute concurrently via `Task.WhenAll`. Dependent steps wait for their prerequisites. |
| **Human-in-the-Loop (HITL)** | Write, delete, and execute operations are gated through `IApprovalGate`. Default-deny on timeout. Full audit trail. |
| **Result Caching** | Identical `(module, input)` pairs return cached output, avoiding redundant LLM calls or side effects. |
| **Compound Requests** | Multi-intent messages are automatically split and each sub-task runs through the full pipeline independently. |
| **Conversation History** | In-memory conversation history (last 10 turns) provides context-aware routing and execution across turns. |
| **Module Extensibility** | Implement `IVitruvianModule`, register via DI or drop a DLL into `plugins/` — the GOAP planner discovers it automatically. |
| **Security** | Linux-style permissions, HITL approval, sandboxed execution with resource limits, and signed-plugin enforcement. |

---

## Architecture

Every user request passes through three phases:

```
  User Request
       │
       ▼
  ┌──────────┐
  │   PLAN   │  GoapPlanner builds an ExecutionPlan
  └────┬─────┘  (PlanSteps + dependency edges)
       │
       ▼
  ┌──────────┐  PlanExecutor runs steps in waves:
  │ EXECUTE  │  • Cache check → HITL gate → Context injection
  │          │  • Module.ExecuteAsync → Cache store
  └────┬─────┘  Independent steps run in parallel
       │
       ▼
  ┌──────────┐
  │  MEMORY  │  Store plan result + conversation turn
  └────┬─────┘
       │
       ▼
    Response
```

The two core abstractions:

- **`IVitruvianModule`** — every capability (built-in or third-party) implements this single interface.
- **`GoapPlanner`** — takes a request and the list of registered modules, produces an `ExecutionPlan` with `PlanStep` nodes and `DependsOn` edges.

For a deeper dive see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Building a Module

Create a `net8.0` class library, reference `Vitruvian.Abstractions`, and implement `IVitruvianModule`:

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

Register via DI or drop the compiled DLL into `plugins/`. See [docs/EXTENDING.md](docs/EXTENDING.md) for the complete guide including SDK attributes, permissions, and API key declarations.

---

## Using Tools with IModelClient

Modules interact with AI models through the `IModelClient` interface. When a module needs the model to call specific tools (functions), Vitruvian provides a fluent API for defining tools and managing the tool execution loop.

### Defining Tools with ModelToolBuilder

Instead of manually constructing parameter dictionaries, use `ModelToolBuilder`:

```csharp
using VitruvianAbstractions.Interfaces;

// Define a function tool with typed parameters
var searchTool = new ModelToolBuilder("search_web", "Search the web for information")
    .AddParameter("query", "The search query text")
    .AddParameter("maxResults", "Maximum number of results to return")
    .Build();

var calculatorTool = new ModelToolBuilder("calculate", "Evaluate a math expression")
    .AddParameter("expression", "The mathematical expression to evaluate")
    .Build();
```

### Automatic Tool Execution Loop

Use `ExecuteWithToolsAsync` to let the model call tools and receive results automatically. The loop continues until the model produces a final text response:

```csharp
// Single callback handler
var result = await modelClient.ExecuteWithToolsAsync(
    systemMessage: "You are a helpful assistant with access to search and calculation tools.",
    userMessage: "What is the population of Denmark divided by 3?",
    tools: [searchTool, calculatorTool],
    toolHandler: async (toolName, toolArgs, ct) =>
    {
        return toolName switch
        {
            "search_web" => await SearchWeb(toolArgs),
            "calculate" => Evaluate(toolArgs),
            _ => $"Unknown tool: {toolName}"
        };
    });

// Or use named handler routing
var handlers = new Dictionary<string, Func<string?, CancellationToken, Task<string>>>
{
    ["search_web"] = async (args, ct) => await SearchWeb(args),
    ["calculate"] = (args, _) => Task.FromResult(Evaluate(args))
};

var result = await modelClient.ExecuteWithToolsAsync(
    "You are a helpful assistant.", userMessage,
    [searchTool, calculatorTool], handlers);
```

### Getting Full Tool Call Information

When you need to inspect which tool the model called (and its arguments), use `CompleteWithToolInfoAsync`:

```csharp
var response = await modelClient.CompleteWithToolInfoAsync(
    systemMessage: "You are a helpful assistant.",
    userMessage: "Search for the latest .NET release",
    tools: [searchTool]);

if (response.ToolCall is not null)
{
    Console.WriteLine($"Model wants to call: {response.ToolCall}");
    Console.WriteLine($"With arguments: {response.ToolArguments}");
}
else
{
    Console.WriteLine($"Final answer: {response.Text}");
}
```

---

## MCP (Model Context Protocol) Tools

Vitruvian supports [MCP tools](https://modelcontextprotocol.io/) for connecting to external tool servers. MCP tools are detected automatically when tool parameters contain `server_url` or `connector_id`, and are forwarded as native MCP tools to providers that support them (OpenAI, Anthropic).

### Defining MCP Tools

Use `ModelToolBuilder` with MCP-specific parameters:

```csharp
// Remote MCP server (e.g., a filesystem tool server)
var fileServer = new ModelToolBuilder("filesystem", "Access project files")
    .AddParameter("server_url", "https://mcp.example.com/filesystem")
    .AddParameter("server_label", "project-files")
    .AddParameter("require_approval", "always")
    .Build();

// OpenAI connector-based MCP tool
var slackConnector = new ModelToolBuilder("slack", "Send messages to Slack")
    .AddParameter("connector_id", "conn_abc123")
    .AddParameter("server_label", "team-slack")
    .AddParameter("server_description", "Post messages and read channels from team Slack")
    .Build();

// MCP tool with bearer auth and filtered tool list
var githubServer = new ModelToolBuilder("github", "GitHub repository operations")
    .AddParameter("server_url", "https://mcp.example.com/github")
    .AddParameter("server_label", "github-repos")
    .AddParameter("authorization", "Bearer ghp_xxxx")
    .AddParameter("require_approval", "never")
    .AddParameter("allowed_tools", "list_repos,get_file,search_code")
    .Build();
```

### MCP Tool Parameters Reference

| Parameter | Required | Description |
|-----------|----------|-------------|
| `server_url` | One of `server_url` or `connector_id` | URL of the remote MCP server |
| `connector_id` | One of `server_url` or `connector_id` | OpenAI connector ID |
| `server_label` | No | Display label (defaults to tool name) |
| `server_description` | No | Description (defaults to tool description) |
| `authorization` | No | Auth header value (e.g., `Bearer token`) |
| `require_approval` | No | `"always"`, `"never"`, or JSON approval config |
| `allowed_tools` | No | Comma-separated or JSON array of allowed tool names |

### Using MCP Tools in a Module

```csharp
public sealed class CodeAssistantModule : IVitruvianModule
{
    private readonly IModelClient? _modelClient;

    // MCP tool connecting to a GitHub MCP server
    private static readonly ModelTool GitHubMcpTool = new ModelToolBuilder("github", "GitHub operations")
        .AddParameter("server_url", "https://mcp.example.com/github")
        .AddParameter("server_label", "github")
        .AddParameter("require_approval", "always")
        .Build();

    // Standard function tool
    private static readonly ModelTool FormatCodeTool = new ModelToolBuilder("format_code", "Format source code")
        .AddParameter("code", "The source code to format")
        .AddParameter("language", "Programming language (e.g. csharp, python)")
        .Build();

    public string Domain => "code-assistant";
    public string Description => "Assist with code tasks using GitHub and formatting tools";

    public CodeAssistantModule(IModelClient? modelClient = null) => _modelClient = modelClient;

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null) return "No model configured.";

        // MCP tools are sent to the provider natively; function tools use JSON Schema.
        // The model decides which tools to invoke based on the user request.
        return await _modelClient.ExecuteWithToolsAsync(
            systemMessage: "You are a code assistant. Use GitHub to search repos and format code when asked.",
            userMessage: request,
            tools: [GitHubMcpTool, FormatCodeTool],
            toolHandler: async (toolName, args, innerCt) =>
            {
                // MCP tools are handled by the provider — only function tools reach here
                if (toolName == "format_code") return FormatCode(args);
                return $"Unknown tool: {toolName}";
            },
            cancellationToken: ct);
    }

    private static string FormatCode(string? args) => $"Formatted: {args}";
}
```

### MCP Approvals with HITL

When a provider (OpenAI or Anthropic) returns an `mcp_approval_request`, Vitruvian routes the approval through the configured HITL approval gate (`IApprovalGate`). The user is prompted to approve or deny the tool call, and Vitruvian sends the `mcp_approval_response` back to the provider automatically.

If no approval gate is configured, Vitruvian throws a clear error indicating that MCP approval requires HITL configuration.

---

## Module Configuration

### `--configure-modules` flag

Interactively enable or disable modules at startup:

```bash
vitruvian --configure-modules
```

```
=== Module Configuration ===
  1. [✓] conversation — Answer general questions (core)
  2. [✓] file-operations — Read, write, and list files
  3. [✓] gmail — Read and compose Gmail messages
  4. [ ] shell-command — Execute shell commands
  ...
Toggle (number), 'all', 'none', or 'done': 4
  shell-command: enabled
```

Also available at runtime via the `/configure-modules` command.

Module preferences are persisted to `vitruvian-modules.json` and loaded automatically on subsequent runs. Core modules (e.g. `conversation`) cannot be disabled.

### `--model` shortcut

Quickly switch model providers without editing env files:

```bash
vitruvian --model openai                              # use OpenAI with default model
vitruvian --model anthropic:claude-3-5-sonnet-latest  # use Anthropic with specific model
vitruvian --model gemini:gemini-2.0-flash             # use Gemini with specific model
```

The provider and model are persisted to `.env.Vitruvian` for subsequent runs.

---

## Documentation

All detailed documentation lives in the [`docs/`](docs/) folder:

| Document | Audience | Description |
|----------|----------|-------------|
| [Installation](docs/INSTALL.md) | Everyone | Prerequisites, build, guided & manual setup, plugin installation |
| [Using Vitruvian](docs/USING.md) | Users | Running the CLI, runtime behaviour, compound requests |
| [Architecture](docs/ARCHITECTURE.md) | Developers | GOAP pipeline, key components, execution flow |
| [Extending](docs/EXTENDING.md) | Plugin authors | Writing modules, SDK attributes, permissions, API keys |
| [Governance](docs/GOVERNANCE.md) | Operators / Developers | Scoring model, hysteresis, explainability |
| [Security](docs/SECURITY.md) | Operators / Plugin authors | Permissions, HITL, sandboxing, installation controls |
| [Policy](docs/POLICY.md) | Operators | Policy validation and default behaviour |
| [Operations](docs/OPERATIONS.md) | Operators | Audit, replay, and doctor commands |
| [Compound Requests](docs/COMPOUND-REQUESTS.md) | Developers | Multi-intent detection, decomposition, execution |
| [Contributing](docs/CONTRIBUTING.md) | Contributors | Development setup, project areas, testing |

---

## Repository Layout

```
Vitruvian.sln
├── src/
│   ├── Vitruvian.Abstractions/      ← Core interfaces, enums, facts, planning types
│   ├── Vitruvian.Runtime/           ← GoapPlanner, PlanExecutor, ModuleRouter,
│   │                                   PermissionChecker, CompoundRequestOrchestrator
│   ├── Vitruvian.PluginSdk/         ← SDK attributes for module metadata
│   ├── Vitruvian.PluginHost/        ← Plugin loader (AssemblyLoadContext), sandboxing
│   ├── Vitruvian.Hitl/              ← ConsoleApprovalGate, HITL facts
│   ├── Vitruvian.StandardModules/   ← Built-in modules (File, Conversation, Web, …)
│   ├── Vitruvian.WeatherModule/     ← Example standalone module
│   └── Vitruvian.Cli/               ← CLI entry point, RequestProcessor, ModelClientFactory
├── tests/
│   └── Vitruvian.Tests/             ← xUnit tests
├── docs/                            ← Detailed documentation (see table above)
└── scripts/                         ← Guided setup scripts (install.sh / install.ps1)
```

---

## Configuration

Create a `.env.Vitruvian` file in the project root, or export environment variables before running:

| Variable | Description | Default |
|----------|-------------|---------|
| `VITRUVIAN_MODEL_PROVIDER` | AI provider: `OpenAI`, `Anthropic`, or `Gemini` | — |
| `VITRUVIAN_OPENAI_API_KEY` | OpenAI API key | — |
| `VITRUVIAN_ANTHROPIC_API_KEY` | Anthropic API key | — |
| `VITRUVIAN_GEMINI_API_KEY` | Google Gemini API key | — |
| `VITRUVIAN_MODEL_NAME` | Specific model to use | Provider default |
| `VITRUVIAN_WORKING_DIRECTORY` | File operations directory | `~/Vitruvian-workspace` |
| `VITRUVIAN_MEMORY_CONNECTION_STRING` | SQLite connection string for durable memory | In-memory |

See [docs/INSTALL.md](docs/INSTALL.md) for the full variable reference and profile-based setup.

---

## OpenAI Responses Tools and MCP

When using `VITRUVIAN_MODEL_PROVIDER=OpenAI`, `ModelRequest.Tools` are forwarded to the OpenAI `/v1/responses` payload:

- Non-MCP tools are sent as OpenAI `function` tools with JSON Schema generated from `ModelTool.Parameters`.
- MCP tools (detected via `server_url`, `connector_id`, or `type=mcp` parameter) are sent as native OpenAI `mcp` tools.

For tool creation examples using `ModelToolBuilder`, the execution loop via `ExecuteWithToolsAsync`, and MCP configuration, see [Using Tools with IModelClient](#using-tools-with-imodelclient) and [MCP Tools](#mcp-model-context-protocol-tools) above.

---

## Contributing

Contributions welcome! See [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md) for details.

1. Fork the repository
2. Create a feature branch
3. Make your changes following the existing patterns
4. Write or update tests (target: all tests green)
5. Submit a pull request

---

## License

See LICENSE file for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/mrrasmussendk/Vitruvian/issues)
- **Discussions**: [GitHub Discussions](https://github.com/mrrasmussendk/Vitruvian/discussions)
