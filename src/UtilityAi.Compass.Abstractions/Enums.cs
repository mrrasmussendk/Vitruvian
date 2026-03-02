namespace UtilityAi.Compass.Abstractions;

/// <summary>
/// High-level intent detected from a user request.
/// Used by <see cref="Facts.GoalSelected"/> to drive goal-based routing.
/// </summary>
public enum GoalTag
{
    /// <summary>Provide an answer to a question.</summary>
    Answer,
    /// <summary>Ask a clarifying question before proceeding.</summary>
    Clarify,
    /// <summary>Summarize existing content or conversation.</summary>
    Summarize,
    /// <summary>Execute an action or command.</summary>
    Execute,
    /// <summary>Request human approval before proceeding.</summary>
    Approve,
    /// <summary>Halt processing or end the conversation.</summary>
    Stop
}

/// <summary>
/// Processing lane that categorises a proposal's stage in the pipeline.
/// Used by <see cref="Facts.LaneSelected"/> for lane-based routing.
/// </summary>
public enum Lane
{
    /// <summary>Parse and understand the user's input.</summary>
    Interpret,
    /// <summary>Formulate a plan of action.</summary>
    Plan,
    /// <summary>Carry out the planned action.</summary>
    Execute,
    /// <summary>Deliver a response to the user.</summary>
    Communicate,
    /// <summary>Apply safety checks and guardrails.</summary>
    Safety,
    /// <summary>Perform background maintenance tasks.</summary>
    Housekeeping
}

/// <summary>Indicates the level of side effects a proposal may produce.</summary>
public enum SideEffectLevel
{
    /// <summary>No state changes – safe to execute speculatively.</summary>
    ReadOnly,
    /// <summary>Creates or updates state.</summary>
    Write,
    /// <summary>Permanently removes or irreversibly alters state.</summary>
    Destructive
}

/// <summary>Outcome tag recorded after a proposal has been executed.</summary>
public enum OutcomeTag
{
    /// <summary>The action completed successfully.</summary>
    Success,
    /// <summary>The action failed.</summary>
    Failure,
    /// <summary>The action was skipped (e.g. due to cooldown).</summary>
    Skipped,
    /// <summary>The action was escalated to a human.</summary>
    Escalated
}

/// <summary>Verb for CLI intent detection, indicating the type of CLI operation.</summary>
public enum CliVerb
{
    /// <summary>A read / query operation.</summary>
    Read,
    /// <summary>A write / create operation.</summary>
    Write,
    /// <summary>An update / modify operation.</summary>
    Update
}

/// <summary>Outcome of a single workflow step execution.</summary>
public enum StepOutcome
{
    /// <summary>The step completed successfully.</summary>
    Succeeded,
    /// <summary>The step failed but can be retried.</summary>
    FailedRetryable,
    /// <summary>The step failed fatally and cannot be retried.</summary>
    FailedFatal,
    /// <summary>The step requires additional user input to proceed.</summary>
    NeedsUserInput,
    /// <summary>The step produced output that requires validation.</summary>
    NeedsValidation,
    /// <summary>The step was cancelled before completion.</summary>
    Cancelled
}

/// <summary>Lifecycle state of a workflow run.</summary>
public enum WorkflowStatus
{
    /// <summary>No workflow is active.</summary>
    Idle,
    /// <summary>The workflow is actively executing steps.</summary>
    Active,
    /// <summary>The workflow is blocked waiting for user input.</summary>
    AwaitingUser,
    /// <summary>The workflow is awaiting validation of a step or final result.</summary>
    Validating,
    /// <summary>The workflow is executing a repair sequence.</summary>
    Repairing,
    /// <summary>The workflow completed successfully.</summary>
    Completed,
    /// <summary>The workflow was aborted due to a fatal error or budget exhaustion.</summary>
    Aborted
}

/// <summary>Type of repair action to take when a step or validation fails.</summary>
public enum RepairType
{
    /// <summary>Retry the failed step.</summary>
    RetryStep,
    /// <summary>Re-plan the remaining workflow steps.</summary>
    Replan,
    /// <summary>Switch to an entirely different workflow.</summary>
    SwitchWorkflow,
    /// <summary>Ask the user for guidance.</summary>
    AskUser,
    /// <summary>Escalate to a human-in-the-loop reviewer.</summary>
    Hitl,
    /// <summary>Abort the workflow entirely.</summary>
    Abort
}

/// <summary>Classifies the kind of proposal for workflow-aware selection.</summary>
public enum ProposalKind
{
    /// <summary>A standalone, non-workflow proposal.</summary>
    Atomic,
    /// <summary>Proposes starting a new workflow.</summary>
    StartWorkflow,
    /// <summary>Continues the current workflow step.</summary>
    ContinueStep,
    /// <summary>Validates a step or workflow result.</summary>
    Validate,
    /// <summary>Repairs a failed step or validation.</summary>
    Repair,
    /// <summary>Requests user input to unblock a workflow.</summary>
    AskUser,
    /// <summary>A system-level proposal (e.g. cooldown, housekeeping).</summary>
    System
}

/// <summary>Scope at which validation is applied.</summary>
public enum ValidationScope
{
    /// <summary>Validate the result of a single step.</summary>
    Step,
    /// <summary>Validate the overall workflow result.</summary>
    Workflow
}

/// <summary>Outcome of a validation check.</summary>
public enum ValidationOutcomeTag
{
    /// <summary>Validation passed.</summary>
    Pass,
    /// <summary>Validation failed but the issue is retryable.</summary>
    FailRetryable,
    /// <summary>Validation failed fatally.</summary>
    FailFatal
}

/// <summary>Linux-style file/resource access permissions for modules.</summary>
[Flags]
public enum ModuleAccess
{
    /// <summary>No access.</summary>
    None = 0,
    /// <summary>Permission to read files or resources.</summary>
    Read = 1,
    /// <summary>Permission to write or modify files or resources.</summary>
    Write = 2,
    /// <summary>Permission to execute commands or processes.</summary>
    Execute = 4
}

/// <summary>Classifies the type of operation a module performs, used for HITL gating.</summary>
public enum OperationType
{
    /// <summary>A read-only query that does not modify state.</summary>
    Read,
    /// <summary>Creates or updates state.</summary>
    Write,
    /// <summary>Permanently removes or irreversibly alters state.</summary>
    Delete,
    /// <summary>Performs network communication.</summary>
    Network,
    /// <summary>Executes a command or process.</summary>
    Execute
}
