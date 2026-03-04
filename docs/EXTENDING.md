# Extending UtilityAi.Vitruvian

This guide is for plugin authors extending Vitruvian with new capabilities.

## 1) Create a plugin project

1. Create a `net10.0` class library
2. Reference `UtilityAi.Vitruvian.PluginSdk`
3. Implement `ICapabilityModule` (optionally `ISensor` / `ICliAction`)

## 2) Annotate capability metadata

Use SDK attributes so governance can route and score proposals:

- `VitruvianCapability`
- `VitruvianGoals`
- `VitruvianLane`
- `VitruvianCost`
- `VitruvianRisk`
- `VitruvianCooldown`
- `VitruvianConflicts`

## 3) Declare permissions

Modules that access files, write state, or execute processes must declare their
required permissions using the `[RequiresPermission]` attribute. The runtime
enforces these declarations before execution — undeclared access is denied.

```csharp
using UtilityAi.Vitruvian.Abstractions;
using UtilityAi.Vitruvian.PluginSdk.Attributes;

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

## 4) Declare required API keys

If your module needs API keys or other secrets at runtime, declare them with the
`[RequiresApiKey]` attribute. Each attribute takes the name of the environment
variable that must hold the key:

```csharp
using UtilityAi.Vitruvian.PluginSdk.Attributes;

[RequiresApiKey("WEATHER_API_KEY")]
[RequiresApiKey("GEOCODING_API_KEY")]
public sealed class WeatherModule : IVitruvianModule
{
    public string Domain => "weather";
    public string Description => "Fetches weather data";

    public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        // Read the key at execution time from the environment.
        var apiKey = Environment.GetEnvironmentVariable("WEATHER_API_KEY");
        // ... use apiKey to call the external service ...
        return Task.FromResult("sunny");
    }
}
```

### How it works

| Phase | What happens |
|-------|--------------|
| **Install time** | The installer scans the module DLL for `[RequiresApiKey]` attributes, checks whether each environment variable is already set, and prompts the user for any missing values. Provided values are persisted to the `.env.Vitruvian` file so they survive process restarts. |
| **Startup** | `EnvFileLoader` loads `.env.Vitruvian` before any plugin code runs, making the keys available via `Environment.GetEnvironmentVariable()`. |
| **Module load** | `InstalledModuleLoader` inspects each module for missing API keys and emits `[WARN]` messages for any that are not set. The module is still loaded, but may fail at execution time. |

### Providing keys manually

You can also set the keys yourself before starting Vitruvian:

```bash
# Option 1 — add to .env.Vitruvian
echo 'WEATHER_API_KEY=sk-abc123' >> .env.Vitruvian

# Option 2 — export before running
export WEATHER_API_KEY=sk-abc123
dotnet run --project samples/Vitruvian.SampleHost
```

You may additionally list the same keys in your plugin's
`vitruvian-manifest.json` under `RequiredSecrets` for documentation purposes:

```json
{
  "RequiredSecrets": ["WEATHER_API_KEY", "GEOCODING_API_KEY"]
}
```

## 5) Build and install

Build/publish the plugin and place outputs in a `plugins/` folder next to the host executable, or install via CLI:

```bash
Vitruvian --install-module /absolute/path/MyPlugin.dll
```

## 6) Example

See:

- Plugin example in the root [README.md](../README.md#building-modules)
- Security model in [docs/SECURITY.md](SECURITY.md)
