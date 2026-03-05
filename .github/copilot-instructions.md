# Copilot Instructions for Vitruvian

## Project Overview

**Vitruvian** is a modular, GOAP-driven AI assistant framework built on .NET. It uses Goal-Oriented Action Planning to decompose user requests into dependency-aware execution plans, runs independent steps in parallel, and enforces human-in-the-loop approval, caching, and memory. Third-party modules plug in via a single interface (`IVitruvianModule`). The host handles planning, routing, governance, and security so module authors focus only on capability logic.

**Language / Runtime:** C# 13, .NET 8, nullable enabled, implicit usings enabled.

---

## Repository Layout

```
Vitruvian.sln
├── src/
│   ├── Vitruvian.Abstractions/      ← Core interfaces, enums, facts, planning types
│   ├── Vitruvian.Runtime/           ← GoapPlanner, PlanExecutor, ModuleRouter, DI
│   ├── Vitruvian.PluginSdk/         ← SDK attributes for module metadata
│   ├── Vitruvian.PluginHost/        ← Plugin loader (AssemblyLoadContext), sandboxing
│   ├── Vitruvian.Hitl/              ← ConsoleApprovalGate, HITL facts
│   ├── Vitruvian.StandardModules/   ← Built-in modules (File, Conversation, Web, …)
│   ├── Vitruvian.WeatherModule/     ← Example standalone module
│   └── Vitruvian.Cli/               ← CLI entry point, RequestProcessor
├── tests/
│   └── Vitruvian.Tests/             ← xUnit tests
├── docs/                            ← Detailed documentation
└── scripts/                         ← Guided setup (install.sh / install.ps1)
```

---

## How to Build

> Requires [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download).

```bash
dotnet build Vitruvian.sln
```

---

## How to Test

Tests use **xUnit** and live in `tests/Vitruvian.Tests/`.

```bash
dotnet test Vitruvian.sln
```

To run a single test class:

```bash
dotnet test --filter "FullyQualifiedName~GoapPlannerTests"
```

---

## How to Run

```bash
dotnet run --project src/Vitruvian.Cli
```

For guided setup (model provider + deployment mode):

```bash
./scripts/install.sh
dotnet run --project src/Vitruvian.Cli
```

---

## Coding Conventions

- **Nullable reference types** are enabled project-wide — always annotate types correctly and avoid `null!` suppressions unless truly necessary.
- **Implicit usings** are enabled — do not add redundant `using System;` / `using System.Collections.Generic;` etc.
- Follow existing file-scoped namespace style: `namespace Vitruvian<Project>;` (e.g. `namespace VitruvianRuntime;`, `namespace VitruvianAbstractions;`)
- Use **`sealed`** on leaf classes that are not designed for inheritance.
- New public types in `src/` require corresponding tests in `tests/Vitruvian.Tests/`.
- Test classes use the pattern `<ClassUnderTest>Tests` and use `[Fact]` / `[Theory]` attributes from xUnit.
- Do **not** add `using Xunit;` inside test files — it is included via `<Using Include="Xunit" />` in the test project.

---

## Architecture Patterns

### GOAP Pipeline

Every user request passes through three phases:
1. **Plan** — `GoapPlanner` builds an `ExecutionPlan` (a DAG of `PlanStep` nodes with dependency edges).
2. **Execute** — `PlanExecutor` runs steps in dependency waves (parallel where possible), with cache check, HITL gate, context injection, and module execution.
3. **Memory** — Plan result and conversation turn are stored for future context.

### Key Interfaces

- **`IVitruvianModule`** — every capability implements this: `Domain`, `Description`, `ExecuteAsync`.
- **`IApprovalGate`** — human-in-the-loop approval for write/delete/execute operations.
- **`IModelClient`** — provider-agnostic LLM access (OpenAI, Anthropic, Gemini).

### Plugin Metadata

- Use the attributes from `VitruvianPluginSdk.Attributes` to annotate `IVitruvianModule` implementations:
  `[VitruvianCapability]`, `[VitruvianGoals]`, `[VitruvianLane]`, `[VitruvianCost]`, `[VitruvianRisk]`, `[VitruvianCooldown]`

### Governance

| Concern      | Mechanism                                                                 |
|--------------|---------------------------------------------------------------------------|
| Cooldowns    | Cooldown tracking prevents rapid re-invocation                            |
| Conflicts    | `ConflictIds` / `ConflictTags` checked per tick                           |
| Cost/Risk    | `effectiveScore = utility − (CostWeight × cost) − (RiskWeight × risk)`   |
| Hysteresis   | Previous winner re-selected when score is within `StickinessBonus` margin |

### Plugin Loading

`PluginLoader` (in `Vitruvian.PluginHost`) uses `AssemblyLoadContext` to load DLLs from a `plugins/` folder and discovers all `IVitruvianModule` types automatically.

---

## Writing a Plugin

1. Target **net8.0** in your class library.
2. Reference `Vitruvian.PluginSdk` (and `Vitruvian.Abstractions`).
3. Implement `IVitruvianModule` and decorate with Vitruvian SDK attributes.
4. Build and drop the DLL into `plugins/` next to the CLI executable.

```csharp
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

[VitruvianCapability("my-domain", priority: 5)]
[VitruvianGoals(GoalTag.Answer, GoalTag.Summarize)]
[VitruvianLane(Lane.Communicate)]
[VitruvianCost(0.1)]
[VitruvianRisk(0.0)]
[VitruvianCooldown("my-domain.action", secondsTtl: 30)]
public sealed class MyModule : IVitruvianModule
{
    public string Domain => "my-domain";
    public string Description => "Example module";

    public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
        => Task.FromResult("done");
}
```
