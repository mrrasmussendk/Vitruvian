namespace VitruvianCli.Commands;

/// <summary>
/// Scaffolds a new module project from a template.
/// </summary>
public sealed class NewModuleCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 2 &&
        (string.Equals(args[0], "--new-module", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/new-module", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        var outputPath = args.Length >= 3 ? args[2] : Directory.GetCurrentDirectory();
        Console.WriteLine(ModuleInstaller.ScaffoldNewModule(args[1], outputPath));
        return Task.FromResult(0);
    }
}
