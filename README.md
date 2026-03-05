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
- MCP tools are sent as OpenAI `mcp` tools and support:
  - `server_url` (remote MCP server)
  - `connector_id` (OpenAI connector)
  - `server_label` / `server_description`
  - `authorization`
  - `require_approval` (`"always"`, `"never"`, or JSON object)
  - `allowed_tools` (CSV string or JSON array)

### Current MCP approval limitation

If OpenAI returns an `mcp_approval_request`, Vitruvian currently throws a clear error. Interactive approval-response chaining (`mcp_approval_response`) is not implemented in the CLI integration yet.

For now, configure MCP tools with `require_approval: "never"` when using OpenAI from Vitruvian.

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
