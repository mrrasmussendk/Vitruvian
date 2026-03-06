namespace VitruvianCli.Commands;

/// <summary>
/// Installs a module from a local path or NuGet package.
/// </summary>
public sealed class InstallModuleCommand : ICliCommand
{
    private readonly string _pluginsPath;

    public InstallModuleCommand(string pluginsPath)
    {
        _pluginsPath = pluginsPath;
    }

    public bool CanHandle(string[] args) =>
        args.Length >= 2 &&
        (string.Equals(args[0], "--install-module", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/install-module", StringComparison.OrdinalIgnoreCase));

    public async Task<int> ExecuteAsync(string[] args)
    {
        var allowUnsigned = args.Any(a => string.Equals(a, "--allow-unsigned", StringComparison.OrdinalIgnoreCase));
        var installResult = await ModuleInstaller.InstallWithResultAsync(args[1], _pluginsPath, allowUnsigned, PromptForSecret);
        Console.WriteLine(installResult.Message);
        if (!installResult.Success)
        {
            Environment.ExitCode = 1;
            return 1;
        }

        Console.WriteLine("Restart Vitruvian CLI to load the new module.");
        return 0;
    }

    private static string? PromptForSecret(string secretName)
    {
        Console.Write($"Missing required secret '{secretName}'. Enter value (blank will fail install): ");
        var value = ConsoleHelper.ReadSecretFromConsole();
        Console.WriteLine();
        return value;
    }
}
