namespace UtilityAi.Compass.Abstractions.Interfaces;

/// <summary>
/// Gate that requires human approval before executing write or destructive operations.
/// Implementations may use a console prompt, UI dialog, or external approval service.
/// </summary>
public interface IApprovalGate
{
    /// <summary>
    /// Requests approval for the specified operation.
    /// Returns <c>true</c> if the human approves, <c>false</c> if denied.
    /// A timeout results in denial (default-deny policy).
    /// </summary>
    /// <param name="operation">The type of operation requesting approval.</param>
    /// <param name="description">A human-readable description of the operation.</param>
    /// <param name="moduleDomain">The domain of the module requesting approval.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> ApproveAsync(OperationType operation, string description, string moduleDomain, CancellationToken ct = default);
}

/// <summary>
/// An immutable record of an approval decision, used for audit trails.
/// </summary>
/// <param name="Timestamp">When the decision was made.</param>
/// <param name="ModuleDomain">The domain of the module that requested approval.</param>
/// <param name="Operation">The type of operation.</param>
/// <param name="Description">Human-readable description of the operation.</param>
/// <param name="Approved">Whether the operation was approved.</param>
/// <param name="UserId">The user who made the decision, if known.</param>
public sealed record ApprovalRecord(
    DateTimeOffset Timestamp,
    string ModuleDomain,
    OperationType Operation,
    string Description,
    bool Approved,
    string? UserId = null);
