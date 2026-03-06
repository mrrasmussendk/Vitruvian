namespace VitruvianCli.Commands;

/// <summary>
/// Replays a previous audit entry. Currently a placeholder — the audit feature was removed with the old architecture.
/// </summary>
public sealed class ReplayCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 2 &&
        (string.Equals(args[0], "replay", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--replay", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine($"Replay accepted for audit id '{args[1]}'.");
        Console.WriteLine(args.Any(a => string.Equals(a, "--no-exec", StringComparison.OrdinalIgnoreCase))
            ? "Replay mode: selection-only (no side effects)."
            : "Replay mode: side effects disabled by default in this build.");
        return Task.FromResult(0);
    }
}
