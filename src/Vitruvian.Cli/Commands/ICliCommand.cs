namespace VitruvianCli.Commands;

/// <summary>
/// Represents a CLI startup command that can be invoked from the command line.
/// Each command handles one or more startup arguments and runs to completion before the process exits.
/// </summary>
public interface ICliCommand
{
    /// <summary>
    /// Determines whether this command can handle the given startup arguments.
    /// </summary>
    bool CanHandle(string[] args);

    /// <summary>
    /// Executes the command with the given startup arguments.
    /// Returns an exit code (0 for success, non-zero for failure).
    /// </summary>
    Task<int> ExecuteAsync(string[] args);
}
