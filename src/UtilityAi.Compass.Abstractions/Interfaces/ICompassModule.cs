namespace UtilityAi.Compass.Abstractions.Interfaces;

/// <summary>
/// Simplified module interface for capability execution.
/// Modules declare what they do via natural language descriptions and execute directly when selected by the router.
/// </summary>
public interface ICompassModule
{
    /// <summary>Gets the unique domain identifier for this module (e.g., "file-operations", "conversation").</summary>
    string Domain { get; }

    /// <summary>Gets the natural language description of what this module does for LLM routing.</summary>
    string Description { get; }

    /// <summary>
    /// Executes this module's capability for the given request.
    /// </summary>
    /// <param name="request">The user's request text.</param>
    /// <param name="userId">Optional user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response text to be returned to the user.</returns>
    Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct);
}
