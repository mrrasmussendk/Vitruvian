namespace VitruvianCli.Commands;

/// <summary>
/// Explains how a policy would be applied to a given request.
/// </summary>
public sealed class PolicyExplainCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 3 &&
        (string.Equals(args[0], "policy", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--policy", StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(args[1], "explain", StringComparison.OrdinalIgnoreCase);

    public Task<int> ExecuteAsync(string[] args)
    {
        var request = string.Join(' ', args.Skip(2));
        var requiresApproval = request.Contains("delete", StringComparison.OrdinalIgnoreCase)
            || request.Contains("write", StringComparison.OrdinalIgnoreCase)
            || request.Contains("update", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(requiresApproval
            ? "Policy explain: matched EnterpriseSafe write/destructive guard; approval required."
            : "Policy explain: matched EnterpriseSafe readonly allow rule.");
        return Task.FromResult(0);
    }
}
