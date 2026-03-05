# Architecture

Vitruvian uses a **Goal-Oriented Action Planning (GOAP)** architecture. Every user request passes through three phases — **Plan**, **Execute**, and **Memory** — before a response is returned.

---

## Table of Contents

- [GOAP Pipeline](#goap-pipeline)
- [Key Components](#key-components)
  - [IVitruvianModule](#ivitruvianmodule--the-module-contract)
  - [GoapPlanner](#goapplanner--plan-before-you-execute)
  - [PlanExecutor](#planexecutor--parallel-governed-execution)
  - [RequestProcessor](#requestprocessor--the-orchestrator)
  - [ModuleRouter](#modulerouter--intelligent-selection)
- [Planning Types](#planning-types)
- [Execution Flow](#execution-flow)

---

## GOAP Pipeline

```
  User Request
       │
       ▼
  ┌──────────┐
  │   PLAN   │  GoapPlanner builds an ExecutionPlan
  └────┬─────┘  (PlanSteps + dependency edges)
       │
       ▼
  ┌──────────┐  PlanExecutor runs steps in dependency waves:
  │ EXECUTE  │
  │          │  Wave 1: [s1] [s2]  ◄── independent steps run in parallel
  │          │  Wave 2: [s3]       ◄── depends on s1, waits for it
  │          │
  │          │  Each step:
  │          │  1. Cache check
  │          │  2. HITL gate (for writes)
  │          │  3. Context injection
  │          │  4. Module.ExecuteAsync
  │          │  5. Cache store
  └────┬─────┘
       │
       ▼
  ┌──────────┐
  │  MEMORY  │  Store plan result + conversation turn
  └────┬─────┘
       │
       ▼
    Response
```

---

## Key Components

### `IVitruvianModule` — The Module Contract

Every capability — built-in or third-party — implements this single interface:

```csharp
public interface IVitruvianModule
{
    string Domain { get; }        // Unique identifier, e.g. "file-operations"
    string Description { get; }   // Natural language description for the planner
    Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct);
}
```

The `Domain` string is used by the planner to assign steps to modules. The `Description` is shown to the LLM so it can reason about which module handles a given sub-task.

### `GoapPlanner` — Plan Before You Execute

The planner receives a user request and the list of registered modules. It calls the LLM to produce an `ExecutionPlan` — a directed acyclic graph of `PlanStep` nodes with `DependsOn` edges. When no LLM is available it falls back to keyword-based single-step plans.

### `PlanExecutor` — Parallel, Governed Execution

Groups steps into **dependency waves**. Steps within a wave have no unmet dependencies and run in parallel via `Task.WhenAll`. Each step passes through:

1. **Cache check** — skip execution if an identical `(module, input)` result already exists.
2. **HITL gate** — write, delete, and execute operations require human approval through `IApprovalGate`.
3. **Context window** — a sliding window of recent step outputs is injected so downstream steps have awareness of prior results.
4. **Module execution** — delegates to `IVitruvianModule.ExecuteAsync`.
5. **Cache store** — the result is cached for future reuse.

After all steps complete, the `PlanResult` is persisted to in-memory storage.

### `RequestProcessor` — The Orchestrator

Wires together the planner, executor, conversation history, and context-aware module wrapping. The executor is reused across requests to preserve cache and memory state. `RequestProcessor` also handles compound-request detection and decomposition (see [Compound Requests](COMPOUND-REQUESTS.md)).

### `ModuleRouter` — Intelligent Selection

Uses LLM-based reasoning to select the best module for a given request. Falls back to keyword matching when no LLM is available. The planner calls the router for module assignment during plan construction.

---

## Planning Types

```csharp
// A single step in an execution plan
public sealed record PlanStep(
    string StepId,
    string ModuleDomain,
    string Description,
    string Input,
    IReadOnlyList<string> DependsOn   // Steps that must complete first
);

// A complete execution plan (the output of GoapPlanner)
public sealed record ExecutionPlan(
    string PlanId,
    string OriginalRequest,
    IReadOnlyList<PlanStep> Steps,     // In topological order
    string? Rationale                  // LLM-provided explanation
);

// Result of executing a single step
public sealed record PlanStepResult(
    string StepId,
    string ModuleDomain,
    bool Success,
    string Output,
    DateTimeOffset ExecutedAt,
    TimeSpan Duration
);

// Aggregated result of an entire plan
public sealed record PlanResult(
    string PlanId,
    bool Success,
    IReadOnlyList<PlanStepResult> StepResults,
    string AggregatedOutput
);
```

---

## Execution Flow

A concrete example:

```
1. User types: "Read notes.txt then summarize it"

2. GoapPlanner produces:
   Step s1: file-operations → "Read notes.txt"       (depends_on: [])
   Step s2: summarization   → "Summarize the content" (depends_on: [s1])

3. PlanExecutor runs:
   Wave 1: executes s1 (file read — no HITL needed for reads)
   Wave 2: executes s2 (gets s1 output via context window)

4. Results aggregated and returned to user.

5. Plan result stored in memory; conversation turn stored in history.
```

Multi-step requests with independent sub-tasks run those sub-tasks in parallel:

```
User: "What is the weather in London?" + "Read notes.txt"

GoapPlanner produces:
  Step s1: weather         → "Weather in London"  (depends_on: [])
  Step s2: file-operations → "Read notes.txt"     (depends_on: [])

PlanExecutor:
  Wave 1: executes s1 and s2 concurrently (no dependencies)
```
