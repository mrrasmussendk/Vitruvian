namespace VitruvianCli.Commands;

/// <summary>
/// Runs the interactive module configuration selection.
/// </summary>
public sealed class ConfigureModulesCommand : ICliCommand
{
    private readonly string _modulesPath;

    public ConfigureModulesCommand(string modulesPath)
    {
        _modulesPath = modulesPath;
    }

    public bool CanHandle(string[] args) =>
        args.Length >= 1 &&
        (string.Equals(args[0], "--configure-modules", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/configure-modules", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        RunConfigureModules();
        return Task.FromResult(0);
    }

    /// <summary>
    /// Runs the interactive module configuration flow.
    /// This method is also used by the interactive CLI via <see cref="CliHostedService"/>.
    /// </summary>
    internal void RunConfigureModules()
    {
        var standardModules = ModuleSelector.GetStandardModuleInfos();
        var discoveredModules = ModuleSelector.DiscoverModulesFromFolder(_modulesPath);
        var allModules = standardModules.Concat(discoveredModules).ToList();
        var prefs = ModulePreferences.Load();
        prefs = ModuleSelector.RunInteractiveSelection(allModules, prefs);
        prefs.Save();
        Console.WriteLine("Module preferences saved.");
    }
}
