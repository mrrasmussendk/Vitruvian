# Using Vitruvian

This guide covers day-to-day usage of Vitruvian as an AI assistant.

---

## 1. Install and Configure

Follow [INSTALL.md](INSTALL.md) for prerequisites, build/test, and provider setup.

---

## 2. Run the CLI

```bash
dotnet run --project src/Vitruvian.Cli
```

You will see an interactive prompt:

```
Vitruvian CLI started. Type a request (or 'quit' to exit):
>
```

---

## 3. Making Requests

Type natural language requests at the prompt:

```
> What is the weather tomorrow?
> Read the file notes.txt
> Create a file called todo.txt with content "Buy milk"
```

### How requests are processed

For each request, Vitruvian:

1. **Plans** — the `GoapPlanner` decomposes the request into an `ExecutionPlan` of `PlanStep` nodes with dependency edges.
2. **Executes** — the `PlanExecutor` runs steps in dependency waves. Independent steps run in parallel. Each step goes through cache check → HITL approval (for writes) → context injection → module execution → cache store.
3. **Remembers** — the plan result and conversation turn are stored in memory for future context.

For more on the internals, see [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 4. CLI Commands

| Command | Description |
|---------|-------------|
| `/help` | Show available commands |
| `/setup` | Run guided setup |
| `/list-modules` | List all registered modules |
| `/install-module <path>` | Install a plugin module from a DLL path |
| `/load-module <path>` | Load a local module DLL for the current session (debugging) |
| `/unregister-module <domain or filename>` | Unregister a module and remove installed plugin DLLs |
| `/new-module <Name>` | Scaffold a new module project |
| `quit` | Exit the CLI |

---

## 5. Conversation Context

Vitruvian maintains in-memory conversation history (last 10 turns), so follow-up messages are understood in context:

```
> What is the weather tomorrow?
Assistant: I need your location. What city are you in?

> Copenhagen
Assistant: [Provides Copenhagen weather forecast]
```

---

## 6. Compound Requests

Vitruvian handles multi-intent messages automatically when a model client is configured. You can combine multiple independent tasks in a single sentence:

```
> Create file u.txt with gold then give me the colors of the rainbow
> Write hello to greeting.txt and then summarize today's news
```

Each sub-task is routed through the full pipeline, so the right module handles each part — no module needs special compound-request awareness. See [COMPOUND-REQUESTS.md](COMPOUND-REQUESTS.md) for details.

---

## 7. HITL Approval

Write, delete, and execute operations are gated through human approval. When a module requests a side-effecting action, you will see a prompt:

```
[APPROVAL REQUIRED] Module 'file-operations' wants to perform: Write
  Description: Create file todo.txt
  Approve? (y/n):
```

Type `y` to approve or `n` to deny. If you do not respond within the timeout (default: 30 seconds), the operation is denied automatically.

---

## 8. Plugin Installation

Install a plugin module interactively:

```
> /install-module /absolute/path/MyPlugin.dll
```

If a plugin manifest includes `requiredSecrets`, Vitruvian prompts for missing values during interactive install. See [EXTENDING.md](EXTENDING.md) for how to build plugins.

For local module debugging without installation, load the compiled DLL directly:

```
> /load-module /absolute/path/MyPlugin/bin/Debug/net8.0/MyPlugin.dll
```

When done, unregister it from the running session:

```
> /unregister-module my-plugin-domain
```
