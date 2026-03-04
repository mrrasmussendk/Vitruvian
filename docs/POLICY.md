# Policy

Vitruvian supports configurable policies that control which operations are allowed, denied, or require approval. Policies are defined as JSON files and validated at runtime.

---

## Commands

| Command | Description |
|---------|-------------|
| `policy validate <policyFile>` | Validate a policy file against the expected schema |
| `policy explain <request>` | Explain which policy rules match a given request |

---

## Policy File Schema

Policy files are JSON and must contain a top-level `rules` array:

```json
{
  "name": "EnterpriseSafe",
  "rules": [
    { "id": "readonly-allow" }
  ]
}
```

### Validation Outcomes

| Result | Condition |
|--------|-----------|
| **Success** | Policy contains a valid JSON object with a `rules` array |
| **Failure** | File missing, malformed JSON, or missing `rules` array |

---

## Default Behaviour (EnterpriseSafe)

When no custom policy is configured, `policy explain` uses **EnterpriseSafe** defaults:

- **Read-only** requests → allowed.
- **Write / destructive** requests → approval required.

Requests containing keywords like `write`, `update`, or `delete` are treated as approval-required signals.

---

## Example

```bash
policy explain "delete build artifacts under /tmp"
```

Output:

```
Policy explain: matched EnterpriseSafe write/destructive guard; approval required.
```

See [GOVERNANCE.md](GOVERNANCE.md) for how policies interact with the broader governance pipeline.
