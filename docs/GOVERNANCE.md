# Governance

Vitruvian enforces governance over module proposals to ensure safe, predictable, and explainable behaviour. Every proposal passes through an explicit governance pipeline before execution.

---

## Pipeline Overview

```
1. Proposal generation       ← Modules propose actions based on user intent
2. Goal/lane filtering       ← Only proposals matching the current GoalTag + Lane pass
3. Conflict & cooldown       ← Conflicting proposals are eliminated; cooled-down modules are skipped
4. Policy & trust checks     ← Module signing, manifest validation at install time
5. Cost/risk scoring         ← Effective score computed with cost and risk penalties
6. Execution & audit         ← Winning proposal executes; decision is recorded
```

---

## Effective Scoring Model

The governed selection strategy computes an effective score for each proposal:

```
effectiveScore = utility − (costWeight × trustedCost) − (riskWeight × trustedRisk)
```

Where `trustedCost` and `trustedRisk` are bounded by minimum values based on the module's `SideEffectLevel`:

| Side-Effect Level | Minimum Cost | Minimum Risk |
|-------------------|-------------|-------------|
| `ReadOnly` | 0.0 | 0.0 |
| `Write` | 0.2 | 0.35 |
| `Destructive` | 0.4 | 0.7 |

This prevents modules from under-reporting risk for high-impact actions. The highest effective score wins.

---

## Hysteresis

To reduce oscillation between near-equal proposals, the previous winner may be retained when:

```
lastWinnerScore + stickinessBonus >= bestScore − hysteresisEpsilon
```

This keeps behaviour stable under noisy utility differences — the system won't flip between two modules that score nearly the same.

---

## Conflict Resolution

Modules can declare conflict IDs and tags via `[VitruvianConflicts]`. When two proposals share a conflict ID or tag, only the higher-scoring one is kept. This prevents contradictory actions from executing in the same tick.

---

## Cooldowns

Modules can declare cooldowns via `[VitruvianCooldown]`. After a module executes, it cannot be selected again until the cooldown period expires. This prevents rapid re-invocation of expensive or rate-limited actions.

---

## Explainability

Vitruvian surfaces deterministic explanations through CLI commands:

| Command | What it shows |
|---------|--------------|
| `inspect-module <path>` | Manifest, signing status, declared capabilities and permissions |
| `policy explain <request>` | Which policy rules match the request (EnterpriseSafe defaults) |
| `audit list` | All recorded execution decisions |
| `audit show <id>` | Details of a specific execution record |

See [OPERATIONS.md](OPERATIONS.md) for full command details and [POLICY.md](POLICY.md) for the policy framework.
