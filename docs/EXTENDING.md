# Extending Vitruvian

This guide is for plugin authors who want to add new capabilities to Vitruvian.

---

## Table of Contents

- [Create a Plugin Project](#1-create-a-plugin-project)
- [Implement IVitruvianModule](#2-implement-ivitruvianmodule)
- [Add SDK Attributes](#3-add-sdk-attributes)
- [Declare Permissions](#4-declare-permissions)
- [Declare Required API Keys](#5-declare-required-api-keys)
- [Build and Install](#6-build-and-install)
- [Module Best Practices](#7-module-best-practices)

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

## 6. Build and Install

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

## 7. Module Best Practices

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
