namespace UtilityAi.Compass.Abstractions.Facts;

/// <summary>Fact published by the goal router sensor when a goal is detected from user input.</summary>
/// <param name="Goal">The detected <see cref="GoalTag"/>.</param>
/// <param name="Confidence">Confidence score in the range [0, 1].</param>
/// <param name="Reason">Optional human-readable explanation for the classification.</param>
public sealed record GoalSelected(GoalTag Goal, double Confidence, string? Reason = null);

/// <summary>Fact published by the lane router sensor indicating the active processing lane.</summary>
/// <param name="Lane">The selected <see cref="Abstractions.Lane"/>.</param>
public sealed record LaneSelected(Lane Lane);

/// <summary>Unique correlation identifier assigned to a single evaluation tick.</summary>
/// <param name="Value">The correlation identifier string.</param>
public sealed record CorrelationId(string Value);

/// <summary>Fact representing the incoming user message to be processed.</summary>
/// <param name="Text">The raw text of the user's message.</param>
/// <param name="UserId">Optional identifier of the user who sent the message.</param>
public sealed record UserRequest(string Text, string? UserId = null);

/// <summary>Fact representing the response produced by the chosen action.</summary>
/// <param name="Text">The generated response text.</param>
/// <param name="CorrelationId">Optional correlation identifier linking this response to its originating tick.</param>
public sealed record AiResponse(string Text, string? CorrelationId = null);

/// <summary>Fact indicating the user request contains multiple sub-goals requiring sequential execution.</summary>
/// <param name="OriginalRequest">The full user request text.</param>
/// <param name="EstimatedSteps">Estimated number of actions needed to fulfill the request.</param>
/// <param name="IsCompound">Whether the request contains multiple distinct intents.</param>
public sealed record MultiStepRequest(string OriginalRequest, int EstimatedSteps, bool IsCompound);

/// <summary>Fact indicating the goal has been completed and orchestration should stop.</summary>
/// <param name="Confidence">Confidence that the goal is satisfied (0-1).</param>
/// <param name="Reason">Explanation for why the goal is considered complete.</param>
public sealed record GoalCompleted(double Confidence, string Reason);

/// <summary>Fact published by module router with LLM-selected relevant modules for the current request.</summary>
/// <param name="ModuleDomains">List of module domain IDs that are relevant to fulfilling this request.</param>
/// <param name="Confidence">Confidence score in the routing decision [0, 1].</param>
public sealed record RelevantModules(IReadOnlyList<string> ModuleDomains, double Confidence);
