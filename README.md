# UtilityAi.Compass

**UtilityAi.Compass** is a simplified LLM-based assistant framework with intelligent module routing and conversation context.

Compass uses natural language to route user requests to specialized modules (file operations, web search, conversation, etc.) and maintains conversation context for natural multi-turn interactions.

**Try it now (30 seconds):**

```bash
git clone https://github.com/mrrasmussendk/Compass.git
cd Compass
dotnet run --framework net8.0 --project src/UtilityAi.Compass.Cli
```

---

## Table of Contents

- [Who this is for](#who-this-is-for)
- [Quick start](#quick-start)
- [Features](#features)
- [Architecture](#architecture)
- [CLI Usage](#cli-usage)
- [Building Modules](#building-modules)
- [Security & Permissions](#security--permissions)
- [Working Directory](#working-directory)
- [Configuration](#configuration)
- [Repository Layout](#repository-layout)

---

## Who this is for

| Your goal | Start here |
|---|---|
| **Use Compass CLI** | [Quick start](#quick-start) → [CLI Usage](#cli-usage) |
| **Build custom modules** | [Building Modules](#building-modules) |
| **Understand the architecture** | [Architecture](#architecture) |

---

## Quick start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- Git
- API key for OpenAI, Anthropic, or Google Gemini

### Clone and build

```bash
git clone https://github.com/mrrasmussendk/Compass.git
cd Compass
dotnet build
dotnet test
```

### Configure your model provider

Set environment variables for your AI provider:

**OpenAI:**
```bash
export COMPASS_MODEL_PROVIDER=OpenAI
export COMPASS_OPENAI_API_KEY=sk-...
export COMPASS_MODEL_NAME=gpt-4  # Optional, defaults to gpt-4
```

**Anthropic:**
```bash
export COMPASS_MODEL_PROVIDER=Anthropic
export COMPASS_ANTHROPIC_API_KEY=sk-ant-...
export COMPASS_MODEL_NAME=claude-3-5-sonnet-20241022  # Optional
```

**Google Gemini:**
```bash
export COMPASS_MODEL_PROVIDER=Gemini
export COMPASS_GEMINI_API_KEY=...
export COMPASS_MODEL_NAME=gemini-2.0-flash-exp  # Optional
```

### Run Compass

```bash
dotnet run --project src/UtilityAi.Compass.Cli
```

You should see:
```
Compass CLI started. Type a request (or 'quit' to exit):
Model provider configured: OpenAi (gpt-4)
Working directory: C:\Users\YourName\compass-workspace
>
```

Try some requests:
```
> What is the weather tomorrow?
> Create a file called notes.txt with content "Hello World"
> Summarize this text: [paste some text]
```

---

## Features

### 🎯 **Intelligent Module Routing**
- LLM-based routing selects the best module for each request
- Fallback to simple keyword matching when LLM unavailable
- Supports compound requests (automatically splits and executes multiple steps)

### 💬 **Conversation Context**
- Maintains in-memory conversation history (last 10 turns)
- Context-aware routing and execution
- Natural follow-up questions work seamlessly

### 📁 **File Operations**
- Read and write files in a dedicated working directory
- Default location: `~/compass-workspace`
- Configurable via `COMPASS_WORKING_DIRECTORY` environment variable

### 🔍 **Web Search**
- Search the web for current information, weather, news
- Uses LLM with web search capabilities

### 📧 **Gmail Integration**
- Read Gmail messages
- Create draft replies (requires OAuth setup)

### 📝 **Text Summarization**
- Summarize long documents, conversations, or any text

### 🗨️ **General Conversation**
- Fallback module for general Q&A
- Powered by your configured LLM

### 🔒 **Security & Permissions**
- Linux-style permission model (read/write/execute) with user/group/other tiers
- Human-in-the-loop approval gate for write and destructive operations
- Module sandboxing with configurable resource limits (CPU, memory, wall time)
- Deny-by-default policy across all security layers

---

## Architecture

### Simplified Design

Compass uses a **simplified module-based architecture** without the UtilityAI orchestration layer:

```
User Input
    ↓
[Conversation Context Added]
    ↓
[LLM Router] → Selects best module based on description
    ↓
[Module Execution] → Executes with full context
    ↓
Response
```

### Key Components

**1. ICompassModule Interface**
```csharp
public interface ICompassModule
{
    string Domain { get; }           // e.g., "file-operations"
    string Description { get; }      // Natural language description
    Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct);
}
```

**2. ModuleRouter**
- Uses LLM to select the best module based on request and module descriptions
- Falls back to keyword matching if LLM unavailable
- Considers cost and risk metadata when available

**3. RequestProcessor**
- Manages conversation history
- Enriches requests with context
- Handles compound request orchestration

**4. Standard Modules**
- `FileOperationsModule` - File I/O
- `WebSearchModule` - Web search
- `ConversationModule` - General Q&A
- `SummarizationModule` - Text summarization
- `GmailModule` - Email operations

---

## CLI Usage

### Interactive Mode

Start Compass and type natural language requests:

```bash
> Read the file notes.txt
> What is the weather in Copenhagen?
> Summarize this article: [paste text]
```

### Commands

- `/help` - Show available commands
- `/setup` - Run guided setup (if available)
- `/list-modules` - List registered modules
- `quit` - Exit

### Conversation Flow

Compass maintains context across messages:

```
> What is the weather tomorrow?
Assistant: I need your location. What city are you in?

> Copenhagen
Assistant: [Provides Copenhagen weather forecast]
```

The second message "Copenhagen" is understood in the context of the weather question.

---

## Building Modules

### Create a Module

Modules implement the `ICompassModule` interface:

```csharp
using UtilityAi.Compass.Abstractions.Interfaces;

public sealed class MyModule : ICompassModule
{
    private readonly IModelClient? _modelClient;

    public string Domain => "my-domain";
    public string Description => "Describe what your module does for LLM routing";

    public MyModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        // Your implementation here
        if (_modelClient is null)
            return "No model configured.";

        // Use the LLM to help process the request
        return await _modelClient.GenerateAsync(request, ct);
    }
}
```

### Register Your Module

In `Program.cs`:

```csharp
builder.Services.AddSingleton<ICompassModule>(sp =>
    new MyModule(sp.GetService<IModelClient>()));
```

### Best Practices

1. **Clear Descriptions**: Write descriptions that help the LLM router understand when to use your module
   - Good: `"Search the web for current information, weather forecasts, news, and real-time data"`
   - Bad: `"Web stuff"`

2. **Handle Context**: The `request` parameter includes conversation context when available
   - Parse context markers like `[Recent conversation context:...]`

3. **Use IModelClient**: Inject and use `IModelClient` for LLM capabilities

4. **Error Handling**: Return user-friendly error messages

---

## Security & Permissions

Compass enforces a layered security model for all module execution. For the full reference, see [`docs/SECURITY.md`](docs/SECURITY.md).

### Permission Model

Modules declare required access using the `[RequiresPermission]` attribute. The runtime validates declarations against the active `IPermissionContext` before execution.

```csharp
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.PluginSdk.Attributes;

[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Write, resource: "files/*")]
public sealed class SecureFileModule : ICompassModule
{
    public string Domain => "secure-files";
    public string Description => "Secure file operations with declared permissions";

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        // Module implementation — runtime enforces permissions before this runs
        return "done";
    }
}
```

Enforcement at runtime:

```csharp
var checker = new PermissionChecker(permissionContext);
checker.Enforce(module, userId, group); // throws PermissionDeniedException if denied
```

### HITL Approval Gate

Write and destructive operations require explicit human approval via `IApprovalGate`:

```csharp
IApprovalGate gate = new ConsoleApprovalGate(timeout: TimeSpan.FromSeconds(30));
bool approved = await gate.ApproveAsync(
    OperationType.Write, "Write config.json", module.Domain);
```

- **Default-deny**: unanswered prompts are automatically denied after timeout.
- **Audit trail**: every decision is recorded as an `ApprovalRecord`.

### Module Sandboxing

Untrusted modules run inside `SandboxedModuleRunner` with enforced resource limits:

```csharp
var runner = new SandboxedModuleRunner(new DefaultSandboxPolicy
{
    MaxWallTime = TimeSpan.FromSeconds(10),
    AllowFileSystem = true
});

string result = await runner.ExecuteAsync(module, request, userId);
```

Default sandbox limits: 30 s CPU, 256 MB memory, 60 s wall time, no file system / network / process access.

---

## Working Directory

### Default Location

Files are stored in: `~/compass-workspace` (or `C:\Users\YourName\compass-workspace` on Windows)

### Custom Location

Set the environment variable:

```bash
export COMPASS_WORKING_DIRECTORY=/path/to/your/workspace
```

### File Operations

```
> Create a file called todo.txt with content "Buy milk"
File created: todo.txt

> Read the content of todo.txt
Buy milk
```

Files are created in the working directory by default. You can also use absolute paths if needed.

---

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `COMPASS_MODEL_PROVIDER` | AI provider: `OpenAI`, `Anthropic`, or `Gemini` | - |
| `COMPASS_OPENAI_API_KEY` | OpenAI API key | - |
| `COMPASS_ANTHROPIC_API_KEY` | Anthropic API key | - |
| `COMPASS_GEMINI_API_KEY` | Google Gemini API key | - |
| `COMPASS_MODEL_NAME` | Specific model to use | Provider default |
| `COMPASS_WORKING_DIRECTORY` | File operations directory | `~/compass-workspace` |

### .env.compass File

Create a `.env.compass` file in the project root:

```bash
COMPASS_MODEL_PROVIDER=OpenAI
COMPASS_OPENAI_API_KEY=sk-...
COMPASS_MODEL_NAME=gpt-4
COMPASS_WORKING_DIRECTORY=/custom/path
```

---

## Repository Layout

```text
UtilityAi.Compass.sln
├── src/
│   ├── UtilityAi.Compass.Abstractions/     # Core interfaces, enums, and facts
│   ├── UtilityAi.Compass.Runtime/          # Routing, orchestration, permission enforcement
│   ├── UtilityAi.Compass.StandardModules/  # Built-in modules
│   ├── UtilityAi.Compass.PluginSdk/        # SDK attributes for module development
│   ├── UtilityAi.Compass.PluginHost/       # Plugin loading, sandboxing, manifests
│   ├── UtilityAi.Compass.Hitl/             # Human-in-the-loop approval gates
│   ├── UtilityAi.Compass.WeatherModule/    # Example weather module
│   └── UtilityAi.Compass.Cli/              # CLI application
├── tests/
│   └── UtilityAi.Compass.Tests/            # Unit tests
└── docs/
    ├── SECURITY.md                          # Security model reference
    ├── EXTENDING.md                         # Plugin development guide
    ├── GOVERNANCE.md                        # Governance pipeline
    └── ...                                  # Additional documentation
```

---

## License

See LICENSE file for details.

---

## Contributing

Contributions welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Write/update tests
5. Submit a pull request

---

## Support

- **Issues**: [GitHub Issues](https://github.com/mrrasmussendk/Compass/issues)
- **Discussions**: [GitHub Discussions](https://github.com/mrrasmussendk/Compass/discussions)
