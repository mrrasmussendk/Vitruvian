# UtilityAi.Compass

**UtilityAi.Compass** is a modular, GOAP-driven AI assistant framework built on .NET. It uses **Goal-Oriented Action Planning** to decompose user requests into dependency-aware execution plans, runs independent steps in parallel, and enforces human-in-the-loop approval, caching, and memory — all before any side-effecting action fires.

Third-party modules plug in via a single interface (`ICompassModule`). The host handles planning, routing, governance, and security so module authors focus only on capability logic.

**Try it now:**

```bash
git clone https://github.com/mrrasmussendk/Compass.git
cd Compass
dotnet run --framework net8.0 --project src/UtilityAi.Compass.Cli
```

---

## Table of Contents

- [Who This Is For](#who-this-is-for)
- [Quick Start](#quick-start)
- [Features](#features)
- [Architecture](#architecture)
  - [GOAP Pipeline](#goap-pipeline)
  - [Key Components](#key-components)
  - [Execution Flow](#execution-flow)
- [Building Your Own Module](#building-your-own-module)
  - [Step 1: Implement ICompassModule](#step-1-implement-icompassmodule)
  - [Step 2: Register via DI](#step-2-register-via-di)
  - [Step 3: Drop-in Plugin (Optional)](#step-3-drop-in-plugin-optional)
  - [Module Best Practices](#module-best-practices)
- [CLI Usage](#cli-usage)
- [Security & Permissions](#security--permissions)
  - [Permission Model](#permission-model)
  - [HITL Approval Gate](#hitl-approval-gate)
  - [Module Sandboxing](#module-sandboxing)
- [Configuration](#configuration)
- [Repository Layout](#repository-layout)
- [Contributing](#contributing)

---

## Who This Is For

| Your goal | Start here |
|---|---|
| **Use Compass as an assistant** | [Quick Start](#quick-start) → [CLI Usage](#cli-usage) |
| **Build and inject custom modules** | [Building Your Own Module](#building-your-own-module) |
| **Understand the GOAP architecture** | [Architecture](#architecture) |
| **Contribute to the framework** | [Repository Layout](#repository-layout) → [Contributing](#contributing) |

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- Git
- API key for OpenAI, Anthropic, or Google Gemini

### Clone, build, and test

```bash
git clone https://github.com/mrrasmussendk/Compass.git
cd Compass
dotnet build
dotnet test   # All tests should pass
```

### Configure your model provider

Set environment variables for your preferred AI provider:

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

Or create a `.env.compass` file in the project root — it is loaded automatically:

```bash
COMPASS_MODEL_PROVIDER=OpenAI
COMPASS_OPENAI_API_KEY=sk-...
COMPASS_MODEL_NAME=gpt-4
```

### Run Compass

```bash
dotnet run --project src/UtilityAi.Compass.Cli
```

```
Compass CLI started. Type a request (or 'quit' to exit):
Model provider configured: OpenAi (gpt-4)
Working directory: ~/compass-workspace
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
| **GOAP Planning** | Decomposes requests into dependency-aware plans *before* execution. Multi-step tasks are broken into independent steps that run in parallel. |
| **Multithreaded Execution** | Independent plan steps execute concurrently via `Task.WhenAll`. Dependent steps wait for their prerequisites. |
| **Human-in-the-Loop (HITL)** | Write, delete, and execute operations are gated through `IApprovalGate`. Default-deny on timeout. Full audit trail. |
| **Result Caching** | Identical `(module, input)` pairs return cached output, avoiding redundant LLM calls or side effects. |
| **Plan Memory** | Every completed plan and its results are stored in memory for future reference and context. |
| **Context Window** | A sliding window of recent step outputs is injected into downstream steps, giving each step awareness of prior results. |
| **Conversation History** | In-memory conversation history (last 10 turns) provides context-aware routing and execution across turns. |
| **Module Extensibility** | Implement `ICompassModule`, register via DI or drop a DLL into `plugins/` — the GOAP planner discovers it automatically. |
| **Security** | Linux-style permissions, HITL approval, and sandboxed execution with resource limits. |

---

## Architecture

### GOAP Pipeline

Compass uses a **Goal-Oriented Action Planning (GOAP)** architecture. Every user request passes through three phases:

```
┌─────────────────────────────────────────────────────────────┐
│                      User Request                           │
└──────────────────────────┬──────────────────────────────────┘
                           │
                    ┌──────▼──────┐
                    │  Phase 1:   │
                    │   PLAN      │  GoapPlanner creates an ExecutionPlan
                    │             │  with PlanSteps and dependency edges
                    └──────┬──────┘
                           │
              ┌────────────▼────────────┐
              │  Phase 2: EXECUTE       │
              │                         │
              │  PlanExecutor runs      │
              │  steps in dependency    │
              │  waves:                 │
              │                         │
              │  Wave 1: [s1] [s2]  ◄── independent steps run in parallel
              │  Wave 2: [s3]       ◄── depends on s1, waits for it
              │                         │
              │  Each step:             │
              │  • Cache check          │
              │  • HITL gate (writes)   │
              │  • Context injection    │
              │  • Module.ExecuteAsync  │
              │  • Cache store          │
              └────────────┬────────────┘
                           │
                    ┌──────▼──────┐
                    │  Phase 3:   │
                    │  MEMORY     │  Store plan result + conversation turn
                    └──────┬──────┘
                           │
              ┌────────────▼────────────┐
              │       Response          │
              └─────────────────────────┘
```

### Key Components

#### `ICompassModule` — The Module Contract

Every capability — built-in or third-party — implements this single interface:

```csharp
public interface ICompassModule
{
    string Domain { get; }        // Unique identifier, e.g. "file-operations"
    string Description { get; }   // Natural language description for the planner
    Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct);
}
```

#### `GoapPlanner` — Plan Before You Execute

The planner receives a user request and the list of registered modules. It uses the LLM to produce a `ExecutionPlan` — a graph of `PlanStep` nodes with `DependsOn` edges. Falls back to keyword-based single-step plans when no LLM is available.

```csharp
// Planning types
record PlanStep(string StepId, string ModuleDomain, string Description,
                string Input, IReadOnlyList<string> DependsOn);

record ExecutionPlan(string PlanId, string OriginalRequest,
                     IReadOnlyList<PlanStep> Steps, string? Rationale);
```

#### `PlanExecutor` — Parallel, Governed Execution

Groups steps into dependency waves. Steps within a wave run in parallel via `Task.WhenAll`. Each step passes through:

1. **Cache check** — skip execution if an identical result exists
2. **HITL gate** — write/delete/execute operations require human approval
3. **Context window** — recent step outputs are injected for downstream awareness
4. **Module execution** — delegates to `ICompassModule.ExecuteAsync`
5. **Cache store** — result is cached for future reuse

After all steps complete, the plan result is persisted to in-memory storage.

#### `RequestProcessor` — The Orchestrator

Wires together the planner, executor, conversation history, and context-aware module wrapping. The executor is reused across requests to preserve cache and memory state.

#### `ModuleRouter` — Intelligent Selection

Uses LLM-based reasoning to select the best module for a request. Falls back to keyword matching when no LLM is available. Used by the planner for module assignment.

### Execution Flow

```
1. User types: "Read notes.txt then summarize it"
2. GoapPlanner produces:
   Step s1: file-operations → "Read notes.txt"       (depends_on: [])
   Step s2: summarization   → "Summarize the content" (depends_on: [s1])
3. PlanExecutor runs:
   Wave 1: executes s1 (file read — no HITL needed for reads)
   Wave 2: executes s2 (gets s1 output via context window)
4. Results aggregated and returned to user
5. Plan result stored in memory; conversation turn stored in history
```

---

## Building Your Own Module

Compass is designed so that anyone can build and inject custom modules. The GOAP planner automatically discovers registered modules and includes them in planning.

### Step 1: Implement `ICompassModule`

Create a class library targeting `net8.0` and reference `UtilityAi.Compass.Abstractions`:

```csharp
using UtilityAi.Compass.Abstractions.Interfaces;

public sealed class TranslationModule : ICompassModule
{
    private readonly IModelClient? _modelClient;

    public string Domain => "translation";
    public string Description => "Translate text between languages using AI";

    public TranslationModule(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        if (_modelClient is null)
            return "No model configured for translation.";

        return await _modelClient.GenerateAsync(
            $"Translate the following as requested: {request}", ct);
    }
}
```

### Step 2: Register via DI

Add your module to the DI container in `Program.cs`. The `RequestProcessor` picks it up automatically:

```csharp
builder.Services.AddSingleton<ICompassModule>(sp =>
    new TranslationModule(sp.GetService<IModelClient>()));
```

That's it. The GOAP planner will now include `translation` as an available module when creating plans. If a user says *"Translate this text to French"*, the planner will route it to your module.

### Step 3: Drop-in Plugin (Optional)

For plugin-based deployment without recompiling the host:

1. Build your module as a class library DLL
2. Drop it into the `plugins/` folder next to the CLI executable
3. Restart Compass — the `PluginHost` discovers and loads it via `AssemblyLoadContext`

You can also use SDK attributes for governance metadata:

```csharp
using UtilityAi.Compass.PluginSdk.Attributes;

[CompassCapability("translation", priority: 5)]
[CompassGoals(GoalTag.Answer)]
[CompassLane(Lane.Execute)]
[CompassCost(0.1)]
[CompassRisk(0.0)]
public sealed class TranslationModule : ICompassModule { /* ... */ }
```

### Module Best Practices

| Practice | Why |
|----------|-----|
| **Write a clear `Description`** | The GOAP planner and LLM router use this to decide when to invoke your module. Be specific: *"Translate text between languages using AI"* not *"Does stuff"*. |
| **Accept `IModelClient?` as optional** | Allows your module to work in environments without an LLM (graceful degradation). |
| **Return user-friendly error messages** | Errors bubble up as plan step results. Clear messages help users understand what happened. |
| **Use `sealed`** | Mark your module class as `sealed` unless inheritance is intentional. |
| **Keep `ExecuteAsync` focused** | One responsibility per module. The GOAP planner handles orchestration across modules. |
| **Declare permissions** | Use `[RequiresPermission]` to declare what access your module needs. The runtime enforces this before execution. |

---

## CLI Usage

### Interactive Mode

```bash
dotnet run --project src/UtilityAi.Compass.Cli
```

Type natural language requests:

```
> Read the file notes.txt
> What is the weather in Copenhagen?
> Create todo.txt with content "Buy milk" then read it back
```

### Commands

| Command | Description |
|---------|-------------|
| `/help` | Show available commands |
| `/setup` | Run guided setup |
| `/list-modules` | List all registered modules |
| `/install-module <path>` | Install a plugin module |
| `/new-module <Name>` | Scaffold a new module project |
| `quit` | Exit |

### Conversation Flow

Compass maintains context across messages:

```
> What is the weather tomorrow?
Assistant: I need your location. What city are you in?

> Copenhagen
Assistant: [Provides Copenhagen weather forecast]
```

The second message is understood in the context of the weather question thanks to the conversation history (last 10 turns).

---

## Security & Permissions

Compass enforces a layered security model. For the full reference, see [`docs/SECURITY.md`](docs/SECURITY.md).

### Permission Model

Modules declare required access using `[RequiresPermission]`. The runtime validates these against the active `IPermissionContext` before execution:

```csharp
[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Write, resource: "files/*")]
public sealed class SecureFileModule : ICompassModule
{
    public string Domain => "secure-files";
    public string Description => "Secure file operations with declared permissions";

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        return "done";
    }
}
```

Enforcement:

```csharp
var checker = new PermissionChecker(permissionContext);
checker.Enforce(module, userId, group); // throws PermissionDeniedException if denied
```

### HITL Approval Gate

The `PlanExecutor` automatically gates write, delete, and execute operations through `IApprovalGate` during plan execution. You can also use it directly:

```csharp
IApprovalGate gate = new ConsoleApprovalGate(timeout: TimeSpan.FromSeconds(30));
bool approved = await gate.ApproveAsync(
    OperationType.Write, "Write config.json", module.Domain);
```

- **Default-deny**: unanswered prompts are automatically denied after timeout
- **Audit trail**: every decision is recorded as an `ApprovalRecord`
- **Plan-level**: HITL runs during plan execution, so the full plan is visible before any side effects fire

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

Default limits: 30 s CPU, 256 MB memory, 60 s wall time, no file system / network / process access.

---

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `COMPASS_MODEL_PROVIDER` | AI provider: `OpenAI`, `Anthropic`, or `Gemini` | — |
| `COMPASS_OPENAI_API_KEY` | OpenAI API key | — |
| `COMPASS_ANTHROPIC_API_KEY` | Anthropic API key | — |
| `COMPASS_GEMINI_API_KEY` | Google Gemini API key | — |
| `COMPASS_MODEL_NAME` | Specific model to use | Provider default |
| `COMPASS_WORKING_DIRECTORY` | File operations directory | `~/compass-workspace` |
| `COMPASS_MEMORY_CONNECTION_STRING` | SQLite connection string for durable memory | In-memory |

### .env.compass File

Create a `.env.compass` file in the project root (loaded automatically):

```bash
COMPASS_MODEL_PROVIDER=OpenAI
COMPASS_OPENAI_API_KEY=sk-...
COMPASS_MODEL_NAME=gpt-4
COMPASS_WORKING_DIRECTORY=/custom/path
```

### Working Directory

File operations use a dedicated directory (default: `~/compass-workspace`). Override with `COMPASS_WORKING_DIRECTORY`.

---

## Repository Layout

```text
UtilityAi.Compass.sln
├── src/
│   ├── UtilityAi.Compass.Abstractions/     # Core interfaces (ICompassModule, IApprovalGate,
│   │                                        #   IModelClient), enums, facts, planning types
│   ├── UtilityAi.Compass.Runtime/          # GoapPlanner, PlanExecutor, ModuleRouter,
│   │                                        #   PermissionChecker, CompoundRequestOrchestrator
│   ├── UtilityAi.Compass.StandardModules/  # Built-in modules (File, Conversation, Web,
│   │                                        #   Summarization, Gmail, Shell)
│   ├── UtilityAi.Compass.PluginSdk/        # SDK attributes for module metadata
│   │                                        #   (CompassCapability, CompassGoals, etc.)
│   ├── UtilityAi.Compass.PluginHost/       # Plugin loading via AssemblyLoadContext,
│   │                                        #   SandboxedModuleRunner
│   ├── UtilityAi.Compass.Hitl/             # ConsoleApprovalGate, HITL facts
│   ├── UtilityAi.Compass.WeatherModule/    # Example standalone module
│   └── UtilityAi.Compass.Cli/              # CLI entry point, RequestProcessor,
│                                            #   ContextAwareModelClient, ModelClientFactory
├── tests/
│   └── UtilityAi.Compass.Tests/            # 68 xUnit tests (planner, executor, router,
│                                            #   modules, permissions, sandboxing, HITL)
└── docs/
    ├── SECURITY.md                          # Security model reference
    ├── EXTENDING.md                         # Plugin development guide
    ├── GOVERNANCE.md                        # Governance pipeline
    └── ...                                  # Additional documentation
```

---

## Contributing

Contributions welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes following the existing patterns
4. Write or update tests (target: all tests green)
5. Submit a pull request

---

## License

See LICENSE file for details.

---

## Support

- **Issues**: [GitHub Issues](https://github.com/mrrasmussendk/Compass/issues)
- **Discussions**: [GitHub Discussions](https://github.com/mrrasmussendk/Compass/discussions)
