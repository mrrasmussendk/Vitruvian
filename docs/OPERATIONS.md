# Operations

Vitruvian provides operational commands for auditing past executions, replaying decisions, and diagnosing configuration issues.

---

## Audit

View past execution records stored in durable memory.

### Commands

| Command | Description |
|---------|-------------|
| `audit list` | List all recorded execution records |
| `audit show <id>` | Show details of a specific record |
| `audit show <id> --json` | Show details in JSON format |

### Typical Workflow

1. Set a durable memory connection string:
   ```bash
   export VITRUVIAN_MEMORY_CONNECTION_STRING="Data Source=/path/to/Vitruvian.db;Pooling=False"
   ```
2. Run requests in Vitruvian.
3. List records: `audit list`
4. Inspect a record: `audit show <id> --json`

---

## Replay

Re-run a previous execution decision without side effects.

| Command | Description |
|---------|-------------|
| `replay <id>` | Replay a past decision |
| `replay <id> --no-exec` | Replay without executing (selection only) |

Replay is selection-focused and defaults to no side effects.

---

## Doctor

Diagnose configuration and operational posture.

| Command | Description |
|---------|-------------|
| `doctor` | Run diagnostic checks |
| `doctor --json` | Output diagnostics in JSON format |

Doctor checks for:

- Missing durable audit configuration (`VITRUVIAN_MEMORY_CONNECTION_STRING`)
- Missing secret provider configuration (`VITRUVIAN_SECRET_PROVIDER`)
- Installed modules that should be inspected or sign-validated

### Recommended Operator Baseline

- Set `VITRUVIAN_MEMORY_CONNECTION_STRING` for durable audit storage
- Set `VITRUVIAN_SECRET_PROVIDER` for secret management
- Run `doctor --json` in CI to fail or alert on insecure posture
