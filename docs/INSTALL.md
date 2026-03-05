# Installation Guide

This guide walks you through installing and running **Vitruvian**.

---

## Prerequisites

| Requirement | Details |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download) | Build and run the solution |
| [Git](https://git-scm.com/) | Clone the repository |

Verify both are installed:

```bash
dotnet --version   # should print 8.x
git --version
```

---

## 1. Clone the Repository

```bash
git clone https://github.com/mrrasmussendk/Vitruvian.git
cd Vitruvian
```

---

## 2. Build

```bash
dotnet build Vitruvian.sln
```

A successful build compiles all projects listed in [Repository Layout](#repository-layout).

---

## 3. Run Tests

```bash
dotnet test Vitruvian.sln
```

To run a single test class:

```bash
dotnet test --filter "FullyQualifiedName~GoapPlannerTests"
```

---

## 4. Configure — Guided Setup (Recommended)

The interactive installer is the easiest way to configure Vitruvian. It supports named profiles and safe re-runs.

**Linux / macOS:**

```bash
./scripts/install.sh
```

**Windows (PowerShell):**

```powershell
.\scripts\install.ps1
```

The host auto-loads `.env.Vitruvian` at startup, so no manual `source` step is required.

### What setup creates

- `.env.Vitruvian.<profile>` for profile-specific values
- `.env.Vitruvian` with `VITRUVIAN_PROFILE=<profile>` to define the active profile

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
   - Default local SQLite: `Data Source=appdb/Vitruvian-memory.db`
   - Third-party connection string

If you select Discord, setup requires both `DISCORD_BOT_TOKEN` and `DISCORD_CHANNEL_ID`.
If you select WebSocket host, setup requires `VITRUVIAN_WEBSOCKET_URL` (for example `ws://0.0.0.0:5005/Vitruvian/`).

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

If you prefer to set environment variables yourself, export the following before running:

| Variable | Required | Description |
|---|---|---|
| `VITRUVIAN_PROFILE` | Recommended | Active profile name (`dev`, `personal`, `team`, `prod`) used to select `.env.Vitruvian.<profile>` |
| `VITRUVIAN_MODEL_PROVIDER` | Yes | `openai`, `anthropic`, or `gemini` |
| `OPENAI_API_KEY` | When provider is `openai` | OpenAI API key |
| `ANTHROPIC_API_KEY` | When provider is `anthropic` | Anthropic API key |
| `GEMINI_API_KEY` | When provider is `gemini` | Gemini API key |
| `VITRUVIAN_MODEL_NAME` | No | Overrides the default model name per provider |
| `VITRUVIAN_MODEL_MAX_TOKENS` | No | Sets Anthropic `max_tokens` (default `512`) |
| `VITRUVIAN_MEMORY_CONNECTION_STRING` | No | Memory/persistence connection string (guided setup default: SQLite file connection) |
| `VITRUVIAN_WORKING_DIRECTORY` | No | Directory used for file operations (default: `~/Vitruvian-workspace`) |
| `DISCORD_BOT_TOKEN` | No | Enables Discord mode |
| `DISCORD_CHANNEL_ID` | No | Target Discord channel (requires `DISCORD_BOT_TOKEN`) |
| `DISCORD_POLL_INTERVAL_SECONDS` | No | Tune Discord polling interval |
| `DISCORD_MESSAGE_LIMIT` | No | Tune Discord message fetch limit |
| `VITRUVIAN_WEBSOCKET_URL` | No | Enables WebSocket host mode (checked before Discord mode) |
| `VITRUVIAN_WEBSOCKET_PUBLIC_URL` | No | Public-facing WebSocket URL shown in startup helpers |
| `VITRUVIAN_WEBSOCKET_DOMAIN` | No | Default domain tag prepended to incoming WebSocket requests |

Example (Linux / macOS):

```bash
export VITRUVIAN_MODEL_PROVIDER=openai
export OPENAI_API_KEY=sk-...
export VITRUVIAN_MEMORY_CONNECTION_STRING="Data Source=appdb/Vitruvian-memory.db"
dotnet run --project src/Vitruvian.Cli
```

---

## 6. Run

```bash
dotnet run --project src/Vitruvian.Cli
```

A REPL will start:

```
Vitruvian CLI started. Type a request (or 'quit' to exit):
> summarize this document
```

If `DISCORD_BOT_TOKEN` and `DISCORD_CHANNEL_ID` are set, the host switches to Discord mode and polls the configured channel for messages.
If `VITRUVIAN_WEBSOCKET_URL` is set, the host starts a WebSocket listener and returns JSON responses.

---

## 7. Install Plugins (Optional)

1. Build the plugin: `dotnet publish -c Release`
2. Copy the output DLL (and any dependencies) into a `plugins/` folder next to the main host executable:

   ```bash
   mkdir -p src/Vitruvian.Cli/bin/Debug/net8.0/plugins
   cp path/to/plugin/bin/Release/net8.0/publish/* \
      src/Vitruvian.Cli/bin/Debug/net8.0/plugins/
   ```

3. Run the host — it will discover all `IVitruvianModule` types automatically.

See [EXTENDING.md](EXTENDING.md) for details on writing your own plugin.

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

## Troubleshooting

| Problem | Fix |
|---|---|
| `dotnet: command not found` | Install the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download) and ensure it is on your `PATH` |
| Build error about target framework | Confirm you have .NET **8** SDK installed (`dotnet --version` → 8.x) |
| Build cannot restore packages | Ensure https://api.nuget.org/v3/index.json is reachable |
| API key errors at runtime | Double-check the correct `*_API_KEY` variable is set for your chosen `VITRUVIAN_MODEL_PROVIDER`; re-run setup and press Enter on key prompt to reuse cached key for existing profile |
| Wrong profile loaded | Confirm `.env.Vitruvian` contains `VITRUVIAN_PROFILE=<name>` and that `.env.Vitruvian.<name>` exists |
| Plugins not discovered | Ensure the DLLs are in a `plugins/` folder next to the running executable |
