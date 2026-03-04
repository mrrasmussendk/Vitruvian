# Installation Guide

This guide walks you through installing and running **UtilityAi.Compass**.

---

## Prerequisites

| Requirement | Details |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download) | Build and run the solution |
| [Git](https://git-scm.com/) | Clone the repository |

Verify both are installed:

```bash
dotnet --version   # should print 10.x
git --version
```

---

## 1. Clone the Repository

Clone the repository:

```bash
git clone https://github.com/mrrasmussendk/Compass.git
cd Compass
```

---

## 2. Build

```bash
dotnet build UtilityAi.Compass.sln
```

A successful build compiles all projects listed in [Solution Layout](#solution-layout).

---

## 3. Run Tests

```bash
dotnet test UtilityAi.Compass.sln
```

To run a single test class:

```bash
dotnet test --filter "FullyQualifiedName~CompassGovernedSelectionStrategyTests"
```

---

## 4. Configure — Guided Setup (Recommended)

The interactive installer is the easiest way to configure Compass. It supports named profiles and safe re-runs.

**Linux / macOS:**

```bash
./scripts/install.sh
```

**Windows (PowerShell):**

```powershell
.\scripts\install.ps1
```

The host auto-loads `.env.compass` at startup, so no manual `source` step is required.

### What setup creates

- `.env.compass.<profile>` for profile-specific values
- `.env.compass` with `COMPASS_PROFILE=<profile>` to define the active profile

Supported profiles:

- `dev`
- `personal`
- `team`
- `prod`

### Setup flow (step-by-step)

1. **Choose onboarding action**
   - Create/update profile configuration
   - Switch active profile
2. **Choose profile** (`dev/personal/team/prod`)
3. **Choose model provider** (OpenAI, Anthropic, Gemini)
4. **Enter provider API key**
   - If re-running setup for an existing profile, you can leave this blank to reuse the cached key already saved in that profile file.
5. **Choose model name** (or accept provider default)
6. **Choose deployment mode**
   - Local console
   - Discord channel
   - WebSocket host
7. **Choose storage mode**
   - Default local SQLite: `Data Source=appdb/compass-memory.db`
   - Third-party connection string

If you select Discord, setup requires both `DISCORD_BOT_TOKEN` and `DISCORD_CHANNEL_ID`.
If you select WebSocket host, setup requires `COMPASS_WEBSOCKET_URL` (for example `ws://0.0.0.0:5005/compass/`).

### Quick profile switch (non-interactive)

Linux/macOS:

```bash
./scripts/install.sh dev
./scripts/install.sh team
```

Windows PowerShell:

```powershell
.\scripts\install.ps1 -Profile dev
.\scripts\install.ps1 -Profile team
```

---

## 5. Configure — Manual Setup

If you prefer to set environment variables yourself, export the following before running the main host:

| Variable | Required | Description |
|---|---|---|
| `COMPASS_PROFILE` | Recommended | Active profile name (`dev`, `personal`, `team`, `prod`) used to select `.env.compass.<profile>` |
| `COMPASS_MODEL_PROVIDER` | Yes | `openai`, `anthropic`, or `gemini` |
| `OPENAI_API_KEY` | When provider is `openai` | OpenAI API key |
| `ANTHROPIC_API_KEY` | When provider is `anthropic` | Anthropic API key |
| `GEMINI_API_KEY` | When provider is `gemini` | Gemini API key |
| `COMPASS_MODEL_NAME` | No | Overrides the default model name per provider |
| `COMPASS_MODEL_MAX_TOKENS` | No | Sets Anthropic `max_tokens` (default `512`) |
| `COMPASS_MEMORY_CONNECTION_STRING` | No | Memory/persistence connection string (guided setup default: SQLite file connection) |
| `DISCORD_BOT_TOKEN` | No | Enables Discord mode |
| `DISCORD_CHANNEL_ID` | No | Target Discord channel (requires `DISCORD_BOT_TOKEN`) |
| `DISCORD_POLL_INTERVAL_SECONDS` | No | Tune Discord polling interval |
| `DISCORD_MESSAGE_LIMIT` | No | Tune Discord message fetch limit |
| `COMPASS_WEBSOCKET_URL` | No | Enables WebSocket host mode (checked before Discord mode) |
| `COMPASS_WEBSOCKET_PUBLIC_URL` | No | Public-facing WebSocket URL shown in startup helpers |
| `COMPASS_WEBSOCKET_DOMAIN` | No | Default domain tag prepended to incoming WebSocket requests |

Example (Linux / macOS):

```bash
export COMPASS_MODEL_PROVIDER=openai
export OPENAI_API_KEY=sk-...
export COMPASS_MEMORY_CONNECTION_STRING="Data Source=appdb/compass-memory.db"
dotnet run --framework net10.0 --project src/UtilityAi.Compass.Cli
```

---

## 6. Run the Sample Host

```bash
dotnet run --framework net10.0 --project src/UtilityAi.Compass.Cli
```

A REPL will start:

```
Compass SampleHost started. Type a request (or 'quit' to exit):
> summarize this document
  Goal: Summarize (85%), Lane: Communicate
> quit
```

If `DISCORD_BOT_TOKEN` and `DISCORD_CHANNEL_ID` are set, the host switches to Discord mode and polls the configured channel for messages.
If `COMPASS_WEBSOCKET_URL` is set, the host starts a WebSocket listener and returns JSON responses with deployment helper hints (`request`, `domain`, `userId`).

---

## 7. Install Plugins (Optional)

1. Build the plugin: `dotnet publish -c Release`
2. Copy the output DLL (and any dependencies) into a `plugins/` folder next to the main host executable:

   ```bash
   mkdir -p src/UtilityAi.Compass.Cli/bin/Debug/net10.0/plugins
   cp path/to/plugin/bin/Release/net10.0/publish/* \
      src/UtilityAi.Compass.Cli/bin/Debug/net10.0/plugins/
   ```

3. Run the main host — it will discover all `ICapabilityModule` and `ISensor` types automatically.

See the [root README](../README.md#step-by-step-build-your-first-capability-module) for details on writing your own plugin.

---

## Solution Layout

```
UtilityAi.Compass.sln
├── src/
│   ├── UtilityAi.Compass.Abstractions/  ← Enums, facts, interfaces
│   ├── UtilityAi.Compass.Runtime/       ← Sensors, modules, strategy, DI
│   ├── UtilityAi.Compass.PluginSdk/     ← Attributes + metadata provider
│   ├── UtilityAi.Compass.PluginHost/    ← Plugin loader (AssemblyLoadContext)
│   ├── UtilityAi.Compass.Hitl/          ← Human-in-the-loop gate (optional)
│   ├── UtilityAi.Compass.StandardModules/ ← Built-in reusable modules
│   ├── UtilityAi.Compass.WeatherModule/   ← Example weather-oriented module
│   └── UtilityAi.Compass.Cli/             ← CLI host/tooling
├── samples/
│   └── Compass.SampleHost/              ← Console REPL / Discord demo host
└── tests/
    └── UtilityAi.Compass.Tests/         ← xUnit tests
```

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Build cannot restore UtilityAi | Ensure https://api.nuget.org/v3/index.json is reachable |
| `dotnet: command not found` | Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download) and ensure it is on your `PATH` |
| Build error about target framework `net10.0` | Confirm you have .NET **10** (not an older SDK) installed |
| API key errors at runtime | Double-check the correct `*_API_KEY` variable is set for your chosen `COMPASS_MODEL_PROVIDER`; re-run setup and press Enter on key prompt to reuse cached key for existing profile |
| Wrong profile loaded | Confirm `.env.compass` contains `COMPASS_PROFILE=<name>` and that `.env.compass.<name>` exists |
| Plugins not discovered | Ensure the DLLs are in a `plugins/` folder next to the running executable |
