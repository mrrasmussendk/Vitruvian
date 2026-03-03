# Extending UtilityAi.Compass

This guide is for plugin authors extending Compass with new capabilities.

## 1) Create a plugin project

1. Create a `net10.0` class library
2. Reference `UtilityAi.Compass.PluginSdk`
3. Implement `ICapabilityModule` (optionally `ISensor` / `ICliAction`)

## 2) Annotate capability metadata

Use SDK attributes so governance can route and score proposals:

- `CompassCapability`
- `CompassGoals`
- `CompassLane`
- `CompassCost`
- `CompassRisk`
- `CompassCooldown`
- `CompassConflicts`

## 3) Declare permissions

Modules that access files, write state, or execute processes must declare their
required permissions using the `[RequiresPermission]` attribute. The runtime
enforces these declarations before execution — undeclared access is denied.

```csharp
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.PluginSdk.Attributes;

[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Write, resource: "files/*")]
public sealed class MyFileModule : ICapabilityModule
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

See [Security — Permission Model](SECURITY.md#permission-model) for the full
enforcement model and runtime API.

## 4) Build and install

Build/publish the plugin and place outputs in a `plugins/` folder next to the host executable, or install via CLI:

```bash
compass --install-module /absolute/path/MyPlugin.dll
```

## 5) Example

See:

- Plugin example in the root [README.md](../README.md#building-modules)
- Security model in [docs/SECURITY.md](SECURITY.md)
