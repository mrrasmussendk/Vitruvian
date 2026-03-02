using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.Hitl;

/// <summary>
/// Console-based <see cref="IApprovalGate"/> that prompts the user for y/n approval
/// on write or destructive operations. Applies a default-deny policy on timeout.
/// </summary>
public sealed class ConsoleApprovalGate : IApprovalGate
{
    private readonly TimeSpan _timeout;
    private readonly List<ApprovalRecord> _auditLog = [];
    private readonly TextReader _input;
    private readonly TextWriter _output;

    /// <summary>Gets the read-only audit trail of all approval decisions.</summary>
    public IReadOnlyList<ApprovalRecord> AuditLog => _auditLog;

    /// <summary>
    /// Initializes a new instance of <see cref="ConsoleApprovalGate"/>.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for approval before default-deny. Defaults to 30 seconds.</param>
    /// <param name="input">Optional text reader for input (defaults to <see cref="Console.In"/>).</param>
    /// <param name="output">Optional text writer for output (defaults to <see cref="Console.Out"/>).</param>
    public ConsoleApprovalGate(TimeSpan? timeout = null, TextReader? input = null, TextWriter? output = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    /// <inheritdoc />
    public async Task<bool> ApproveAsync(OperationType operation, string description, string moduleDomain, CancellationToken ct = default)
    {
        await _output.WriteLineAsync($"[APPROVAL REQUIRED] Module '{moduleDomain}' wants to perform: {operation}");
        await _output.WriteLineAsync($"  Description: {description}");
        await _output.WriteAsync("  Approve? (y/n): ");
        await _output.FlushAsync();

        bool approved;
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await Task.Run(() => _input.ReadLine(), linkedCts.Token);
            approved = string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            await _output.WriteLineAsync("  Timed out – denied by default.");
            approved = false;
        }

        var record = new ApprovalRecord(
            Timestamp: DateTimeOffset.UtcNow,
            ModuleDomain: moduleDomain,
            Operation: operation,
            Description: description,
            Approved: approved);

        _auditLog.Add(record);

        return approved;
    }
}
