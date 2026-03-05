namespace VitruvianAbstractions.Interfaces;

/// <summary>
/// Shared command execution abstraction for modules that need to invoke external processes.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Executes a command with arguments in a specific working directory.
    /// </summary>
    Task<CommandExecutionResult> ExecuteAsync(
        string command,
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from running a command through <see cref="ICommandRunner"/>.
/// </summary>
public sealed record CommandExecutionResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    string? StartError = null);
