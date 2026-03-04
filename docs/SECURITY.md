# Security

Vitruvian implements a layered security model spanning **module permissions**, **human-in-the-loop approval**, **module sandboxing**, and **installation controls**. Every layer follows a **deny-by-default** posture — modules must explicitly declare what they need, and the runtime enforces those declarations before execution.

---

## Table of Contents

- [Permission Model](#permission-model)
- [Human-in-the-Loop (HITL) Approval](#human-in-the-loop-hitl-approval)
- [Module Sandboxing](#module-sandboxing)
- [Installation Controls](#installation-controls)
- [Inspecting Modules](#inspecting-modules)
- [Threat Model](#threat-model)

---

## Permission Model

Vitruvian enforces a Linux-style **user / group / other** permission system for module execution. Modules declare the access levels they require via attributes, and the runtime validates those declarations against the active `IPermissionContext` before execution.

### Access Levels

The `ModuleAccess` flags enum defines three permission levels that can be combined:

| Flag | Value | Description |
|------|-------|-------------|
| `None` | `0` | No access required |
| `Read` | `1` | Permission to read files or resources |
| `Write` | `2` | Permission to create or modify files or resources |
| `Execute` | `4` | Permission to run commands or spawn processes |

### Declaring Permissions

Modules declare required permissions using the `[RequiresPermission]` attribute from `UtilityAi.Vitruvian.PluginSdk`:

```csharp
using UtilityAi.Vitruvian.PluginSdk.Attributes;

[RequiresPermission(ModuleAccess.Read)]
[RequiresPermission(ModuleAccess.Write, resource: "files/*")]
public sealed class MyFileModule : IVitruvianModule { /* ... */ }
```

The attribute supports an optional `resource` parameter for scoping permissions to specific paths or patterns.

### Runtime Enforcement

`PermissionChecker` (in `UtilityAi.Vitruvian.Runtime`) reads the `[RequiresPermission]` attributes from the module type and validates them against the current `IPermissionContext`:

```csharp
var checker = new PermissionChecker(permissionContext);

// Query-style check
bool allowed = checker.IsAllowed(module, userId, group);

// Enforcement — throws PermissionDeniedException if denied
checker.Enforce(module, userId, group);
```

When a module lacks the required permissions, `PermissionDeniedException` is thrown with the module domain and the missing access level.

### Permission Context

The `IPermissionContext` interface provides the three-tier permission model:

| Tier | Description |
|------|-------------|
| **User** | Permissions granted to the owning user |
| **Group** | Permissions granted to members of the user's group |
| **Other** | Permissions granted to all other identities |

The context resolves permissions in order: user → group → other.

### Plugin Manifest Integration

The `PluginManifest` record includes a `RequiredPermissions` field (`ModuleAccess`) that aggregates the permissions required by all modules in a plugin assembly. This allows install-time permission review before any code executes.

---

## Human-in-the-Loop (HITL) Approval

Write and destructive operations require explicit human approval through the `IApprovalGate` interface. This ensures that no module can modify state, send network requests, or execute processes without user consent.

### Operation Types

The `OperationType` enum classifies operations for approval routing:

| Type | Description |
|------|-------------|
| `Read` | Read-only queries — no state changes |
| `Write` | Creates or updates state |
| `Delete` | Permanently removes or irreversibly alters state |
| `Network` | Performs outbound network communication |
| `Execute` | Runs a command or spawns a process |

### Approval Flow

```
Module requests operation
        ↓
 IApprovalGate.ApproveAsync(operation, description, moduleDomain)
        ↓
    ┌────────┐
    │ Human  │ ── Approve (y) ──→ Module executes
    │ Review │ ── Deny (n) ────→ Operation blocked
    │        │ ── Timeout ─────→ Default deny
    └────────┘
```

### Console Implementation

`ConsoleApprovalGate` (in `UtilityAi.Vitruvian.Hitl`) provides a CLI-based approval prompt:

```csharp
IApprovalGate gate = new ConsoleApprovalGate(
    timeout: TimeSpan.FromSeconds(30));

bool approved = await gate.ApproveAsync(
    OperationType.Write,
    "Write configuration to config.json",
    "file-operations");
```

Output:
```
[APPROVAL REQUIRED] Module 'file-operations' wants to perform: Write
  Description: Write configuration to config.json
  Approve? (y/n):
```

**Default-deny policy**: If no response is received within the timeout window (default: 30 seconds), the operation is denied automatically.

### Audit Trail

Every approval decision is recorded as an `ApprovalRecord`:

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | When the decision was made |
| `ModuleDomain` | `string` | The requesting module's domain |
| `Operation` | `OperationType` | The type of operation |
| `Description` | `string` | Human-readable operation description |
| `Approved` | `bool` | Whether the operation was approved |
| `UserId` | `string?` | The user who made the decision |

Access the audit trail via `ConsoleApprovalGate.AuditLog`.

---

## Module Sandboxing

Untrusted modules execute inside a sandboxed runner that enforces resource limits and API restrictions, preventing runaway or malicious plugins from affecting the host.

### Sandbox Policy

The `ISandboxPolicy` interface defines the constraints for sandboxed execution:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxCpuTime` | `TimeSpan` | 30 s | Maximum CPU time per execution |
| `MaxMemoryBytes` | `long` | 256 MB | Maximum memory allocation |
| `MaxWallTime` | `TimeSpan` | 60 s | Maximum wall-clock time per execution |
| `AllowFileSystem` | `bool` | `false` | Whether file system access is permitted |
| `AllowNetwork` | `bool` | `false` | Whether network access is permitted |
| `AllowProcessSpawn` | `bool` | `false` | Whether process creation is permitted |

`DefaultSandboxPolicy` provides these defaults and is configurable via `init` properties.

### Sandboxed Execution

`SandboxedModuleRunner` (in `UtilityAi.Vitruvian.PluginHost`) enforces the sandbox policy during module execution:

```csharp
var policy = new DefaultSandboxPolicy
{
    MaxWallTime = TimeSpan.FromSeconds(10),
    AllowFileSystem = true
};

var runner = new SandboxedModuleRunner(policy);
string result = await runner.ExecuteAsync(module, request, userId);
```

If a module exceeds the wall-time limit, a `TimeoutException` is thrown with a descriptive message.

### Assembly Isolation

`SandboxedModuleRunner.CreateIsolatedContext()` creates a collectible `AssemblyLoadContext` for each plugin, enabling:

- **Type isolation** — plugin types do not leak into the host domain
- **Unloadability** — plugin assemblies can be unloaded when no longer needed
- **Fault isolation** — plugin failures do not crash the host process

```csharp
var context = SandboxedModuleRunner.CreateIsolatedContext("/plugins/MyPlugin.dll");
// Load and execute within the isolated context
context.Unload(); // Release when done
```

---

## Installation Controls

Vitruvian defaults to deny-by-default module installation protections:

- Required plugin manifest: `Vitruvian-manifest.json`
- Unsigned assemblies are blocked by default
- Override for local development only: `--allow-unsigned`

### Manifest Schema

`Vitruvian-manifest.json` must include:

| Field | Type | Required |
|-------|------|----------|
| `publisher` | `string` | Yes |
| `version` | `string` | Yes |
| `capabilities` | `string[]` | Yes (non-empty) |
| `permissions` | `string[]` | Yes |
| `sideEffectLevel` | `string` | Yes |

Optional fields:

| Field | Type | Description |
|-------|------|-------------|
| `integrityHash` | `string` | SHA-256 hash for integrity verification |
| `networkEgressDomains` | `string[]` | Allowed outbound network domains |
| `fileAccessScopes` | `string[]` | Allowed file system paths |
| `requiredSecrets` | `string[]` | Environment variables required at runtime |

### Install Behavior

- **`.dll` install**: Must contain a UtilityAI module type, manifest must exist alongside the DLL, required secrets must be set or entered at the install prompt, and the assembly must be signed unless `--allow-unsigned` is passed.
- **`.nupkg` install**: Must contain compatible assemblies, the package root must contain `Vitruvian-manifest.json`, and the same signing and secrets requirements apply.

Secrets entered at the install prompt are persisted to the `.env.Vitruvian` file so they are available on subsequent runs. See [Extending — Declare required API keys](EXTENDING.md#4-declare-required-api-keys) for details on how modules declare and consume API keys.

---

## Inspecting Modules

Use the `inspect-module` command to review a module's security posture before installation:

```bash
Vitruvian inspect-module <path|package@version>
Vitruvian inspect-module <path|package@version> --json
```

Inspection reports include:

- UtilityAI module type detection
- Assembly signing status
- Manifest presence and validation
- Declared capabilities, permissions, and side-effect levels
- Security findings summary

---

## Threat Model

| Threat | Mitigation |
|--------|------------|
| Untrusted plugin binary | Manifest + signing validation at install time |
| Privilege escalation | `[RequiresPermission]` attribute + `PermissionChecker` enforcement |
| Unauthorized write/delete | `IApprovalGate` human approval with default-deny on timeout |
| Runaway module (CPU/memory) | `SandboxedModuleRunner` with `ISandboxPolicy` resource limits |
| Host process corruption | Collectible `AssemblyLoadContext` isolation per plugin |
| Missing manifest | Blocked at install time |
| Unsigned assembly | Blocked unless explicitly overridden with `--allow-unsigned` |
