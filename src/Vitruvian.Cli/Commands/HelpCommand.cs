namespace VitruvianCli.Commands;

/// <summary>
/// Displays usage information and available CLI commands.
/// </summary>
public sealed class HelpCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 1 &&
        (string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/help", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine("Vitruvian CLI arguments:");
        Console.WriteLine("  --help");
        Console.WriteLine("  --setup");
        Console.WriteLine("  --list-modules");
        Console.WriteLine("  --install-module <path|package@version> [--allow-unsigned]");
        Console.WriteLine("  --unregister-module <domain>");
        Console.WriteLine("  --configure-modules");
        Console.WriteLine("  --model <provider[:model]>  (e.g. --model openai or --model anthropic:claude-3-5-sonnet-latest)");
        Console.WriteLine("  --inspect-module <path|package@version> [--json] (alias: inspect-module)");
        Console.WriteLine("  --doctor [--json] (alias: doctor)");
        Console.WriteLine("  --policy validate <policyFile> (alias: policy validate)");
        Console.WriteLine("  --policy explain <request> (alias: policy explain)");
        Console.WriteLine("  --audit list (alias: audit list)");
        Console.WriteLine("  --audit show <id> [--json] (alias: audit show)");
        Console.WriteLine("  --replay <id> [--no-exec] (alias: replay)");
        Console.WriteLine("  --new-module <Name> [OutputPath]");
        Console.WriteLine();
        Console.WriteLine("Getting started:");
        Console.WriteLine("  1) Vitruvian --setup");
        Console.WriteLine("  2) Vitruvian");
        Console.WriteLine("  3) In interactive mode, type /help for commands or 'quit' to exit.");

        return Task.FromResult(0);
    }
}
