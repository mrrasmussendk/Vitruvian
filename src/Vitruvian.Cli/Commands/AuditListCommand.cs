namespace VitruvianCli.Commands;

/// <summary>
/// Lists audit entries. Currently a placeholder — the audit feature was removed with the old architecture.
/// </summary>
public sealed class AuditListCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 2 &&
        (string.Equals(args[0], "audit", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--audit", StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase);

    public Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine("Audit feature removed with UtilityAI package.");
        Console.WriteLine("Audit functionality was part of the old architecture and is no longer available.");
        return Task.FromResult(1);
    }
}
