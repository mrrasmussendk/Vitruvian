# Contributing to Vitruvian

Thank you for considering contributing to Vitruvian! This guide covers the development workflow and conventions.

---

## Development Setup

```bash
git clone https://github.com/mrrasmussendk/Vitruvian.git
cd Vitruvian
dotnet build Vitruvian.sln
dotnet test Vitruvian.sln
```

---

## Project Areas

| Project | What it contains |
|---------|-----------------|
| `src/Vitruvian.Abstractions` | Core interfaces (`IVitruvianModule`, `IApprovalGate`, `IModelClient`), enums, facts, planning types |
| `src/Vitruvian.Runtime` | `GoapPlanner`, `PlanExecutor`, `ModuleRouter`, `PermissionChecker`, `CompoundRequestOrchestrator`, DI extensions |
| `src/Vitruvian.PluginSdk` | SDK attributes (`[VitruvianCapability]`, `[RequiresPermission]`, etc.) |
| `src/Vitruvian.PluginHost` | Plugin loading via `AssemblyLoadContext`, `SandboxedModuleRunner` |
| `src/Vitruvian.Hitl` | `ConsoleApprovalGate`, HITL facts and audit records |
| `src/Vitruvian.StandardModules` | Built-in modules (File, Conversation, Web, Summarization, …) |
| `src/Vitruvian.WeatherModule` | Example standalone module |
| `src/Vitruvian.Cli` | CLI entry point, `RequestProcessor`, model client factory |
| `tests/Vitruvian.Tests` | xUnit tests covering all components |

---

## Coding Conventions

- **Nullable reference types** are enabled project-wide — annotate types correctly and avoid `null!` suppressions.
- **Implicit usings** are enabled — do not add redundant `using System;` etc.
- Use **file-scoped namespaces**: `namespace VitruvianRuntime;`
- Use **`sealed`** on leaf classes not designed for inheritance.
- Namespaces follow the pattern `Vitruvian<Project>` (e.g. `VitruvianAbstractions`, `VitruvianRuntime`, `VitruvianCli`).
- Target framework is `net8.0`.

---

## Testing Expectations

- Tests live in `tests/Vitruvian.Tests/`.
- New public types in `src/` should have corresponding tests.
- Test classes use the pattern `<ClassUnderTest>Tests` with `[Fact]` / `[Theory]` attributes.
- `using Xunit;` is included via `<Using Include="Xunit" />` in the test project — do not add it manually.
- Run targeted tests for changed areas, then the full suite:

```bash
# Targeted
dotnet test --filter "FullyQualifiedName~GoapPlannerTests"

# Full suite
dotnet test Vitruvian.sln
```

---

## Contribution Workflow

1. Fork the repository.
2. Create a feature branch from `main`.
3. Make your changes following the conventions above.
4. Write or update tests — target: all tests green.
5. Submit a pull request with a clear description of the change.

---

## Contribution Focus Areas

The following areas are good places to contribute:

- **Governance** — scoring, conflict resolution, and cooldown behaviour in `VitruvianGovernedSelectionStrategy`.
- **Planning** — `GoapPlanner` and `PlanExecutor` improvements.
- **Module composition** — how modules are discovered, routed, and composed via DI.
- **Plugin interoperability** — SDK attributes, metadata provider, and plugin loading.
- **Standard modules** — new built-in capabilities or improvements to existing ones.
- **Tests** — expanding coverage for edge cases.
