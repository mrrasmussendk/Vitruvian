namespace UtilityAi.Compass.Abstractions.Planning;

/// <summary>
/// A single step within a GOAP-style execution plan.
/// Each step maps to a module invocation with explicit dependency edges
/// so that independent steps can run in parallel.
/// </summary>
/// <param name="StepId">Unique identifier for this step within the plan.</param>
/// <param name="ModuleDomain">The domain of the module that should handle this step.</param>
/// <param name="Description">Human-readable description of what this step accomplishes.</param>
/// <param name="Input">The request text to pass to the module.</param>
/// <param name="DependsOn">Step IDs that must complete before this step can execute.</param>
public sealed record PlanStep(
    string StepId,
    string ModuleDomain,
    string Description,
    string Input,
    IReadOnlyList<string> DependsOn
);

/// <summary>
/// A GOAP-style execution plan: an ordered, dependency-aware graph of steps
/// produced <em>before</em> any execution begins.
/// </summary>
/// <param name="PlanId">Unique identifier for this plan instance.</param>
/// <param name="OriginalRequest">The user request that triggered plan creation.</param>
/// <param name="Steps">The steps to execute, in topological order.</param>
/// <param name="Rationale">LLM-generated explanation of why this plan was chosen.</param>
public sealed record ExecutionPlan(
    string PlanId,
    string OriginalRequest,
    IReadOnlyList<PlanStep> Steps,
    string? Rationale = null
);

/// <summary>
/// The result of executing a single plan step.
/// </summary>
/// <param name="StepId">The step that was executed.</param>
/// <param name="ModuleDomain">The module domain that handled the step.</param>
/// <param name="Success">Whether the step completed successfully.</param>
/// <param name="Output">The output text produced by the module.</param>
/// <param name="ExecutedAt">Timestamp when execution started.</param>
/// <param name="Duration">How long the step took to execute.</param>
public sealed record PlanStepResult(
    string StepId,
    string ModuleDomain,
    bool Success,
    string Output,
    DateTimeOffset ExecutedAt,
    TimeSpan Duration
);

/// <summary>
/// The aggregated result of executing an entire plan.
/// </summary>
/// <param name="PlanId">The plan that was executed.</param>
/// <param name="Success">Whether all steps completed successfully.</param>
/// <param name="StepResults">Results for each step, in execution order.</param>
/// <param name="AggregatedOutput">Combined output from all steps.</param>
public sealed record PlanResult(
    string PlanId,
    bool Success,
    IReadOnlyList<PlanStepResult> StepResults,
    string AggregatedOutput
);
