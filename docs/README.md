# UtilityAi.Vitruvian

A modular orchestration framework built on top of [UtilityAi](https://github.com/mrrasmussendk/UtilityAi) that adds governance, plugin hosting, goal routing, and human-in-the-loop support.

## Documentation paths

- **Using Vitruvian**: [USING.md](USING.md)
- **Extending Vitruvian**: [EXTENDING.md](EXTENDING.md)
- **Contributing to Vitruvian**: [CONTRIBUTING.md](CONTRIBUTING.md)
- **Installation details**: [INSTALL.md](INSTALL.md)
- **Governance pipeline**: [GOVERNANCE.md](GOVERNANCE.md)
- **Policy reference**: [POLICY.md](POLICY.md)
- **Security model**: [SECURITY.md](SECURITY.md)
- **Operations guide**: [OPERATIONS.md](OPERATIONS.md)

## Installation

See [INSTALL.md](INSTALL.md) for prerequisites, setup, and step-by-step installation instructions.

## Projects

| Project | Description |
|---|---|
| `UtilityAi.Compass.Abstractions` | Shared enums, facts, and interfaces |
| `UtilityAi.Compass.Runtime` | Core sensors, modules, selection strategy, DI extensions |
| `UtilityAi.Compass.PluginSdk` | Attributes and metadata provider for plugin authors |
| `UtilityAi.Compass.PluginHost` | Assembly-based plugin loader and DI integration |
| `UtilityAi.Compass.Hitl` | Human-in-the-loop gate module and facts |
| `UtilityAi.Compass.StandardModules` | Built-in reusable capability modules |
| `UtilityAi.Compass.WeatherModule` | Example weather-focused module |
| `UtilityAi.Compass.Cli` | CLI host/tooling and module workflows |
| `UtilityAi.Compass.Cli` | Console host and primary entry point |

## Quick Start

```csharp
builder.Services.AddUtilityAiCompass(opts =>
{
    opts.EnableGovernanceFinalizer = true;
});
builder.Services.AddSingleton<AttributeMetadataProvider>();
builder.Services.AddSingleton<IProposalMetadataProvider>(sp =>
    sp.GetRequiredService<AttributeMetadataProvider>());
```

## Key Concepts

- **GoalTag**: Classifies user intent (Answer, Clarify, Summarize, Execute, Approve, Stop)
- **Lane**: Routes proposals to processing pipelines (Interpret, Plan, Execute, Communicate, Safety, Housekeeping)
- **CompassGovernedSelectionStrategy**: Selects proposals based on goal/lane filtering, conflict resolution, cooldowns, and cost/risk penalties
- **PluginLoader**: Discovers `ICapabilityModule`, `ISensor`, `IOrchestrationSink`, and `ICliAction` implementations from assemblies
- **HitlGateModule**: Intercepts destructive requests (delete, deploy, override) and routes them through a human approval channel

## CLI Actions

Vitruvian provides discoverable command-line **read**, **write**, and **update** actions that integrate with the shared UtilityAI intent detection and routing pipeline.

### CliVerb Enum

Classifies CLI operations:

| Verb | Description |
|---|---|
| `Read` | Read-only operations (get, show, list, view, fetch) |
| `Write` | Create/insert operations (create, add, set, store, save) |
| `Update` | Modify operations (edit, modify, change, patch, alter) |

### ICliAction Interface

Implement `ICliAction` to define a discoverable CLI action:

```csharp
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.CliAction;

public class ReadConfigAction : ICliAction
{
    public CliVerb Verb => CliVerb.Read;
    public string Route => "config";
    public string Description => "Read configuration values";

    public Task<string> ExecuteAsync(string input, CancellationToken ct = default)
        => Task.FromResult("Current config: ...");
}
```

### Intent Detection

The `CliIntentSensor` automatically detects read/write/update intent from user input using keyword heuristics and publishes a `CliIntent` fact to the EventBus. This works alongside the existing `GoalRouterSensor` and `LaneRouterSensor`.

### Routing

The `CliActionModule` proposes matching `ICliAction` instances as UtilityAI `Proposal` objects. Actions are scored based on verb match and route match, then selected through the standard `CompassGovernedSelectionStrategy` (with goal/lane filtering, conflict resolution, cooldowns, and cost/risk penalties).

### Using CLI Actions from Plugins

Plugins can define CLI actions by implementing `ICliAction`. These are automatically discovered by the `PluginLoader` and registered in the DI container:

```csharp
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.CliAction;
using UtilityAi.Compass.PluginSdk.Attributes;

[CompassCliVerb(CliVerb.Write, "users")]
public class CreateUserAction : ICliAction
{
    public CliVerb Verb => CliVerb.Write;
    public string Route => "users";
    public string Description => "Create a new user";

    public Task<string> ExecuteAsync(string input, CancellationToken ct = default)
        => Task.FromResult("User created successfully.");
}
```

### Manual Registration

CLI actions can also be registered directly in the DI container:

```csharp
builder.Services.AddSingleton<ICliAction>(new ReadConfigAction());
builder.Services.AddSingleton<ICliAction>(new WriteConfigAction());
```

The `CliActionModule` resolves all `ICliAction` registrations from the container and proposes them when a matching `CliIntent` is detected.
