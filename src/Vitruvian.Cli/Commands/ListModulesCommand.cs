using Microsoft.Extensions.DependencyInjection;
using VitruvianPluginHost;

namespace VitruvianCli.Commands;

/// <summary>
/// Lists all installed modules — standard, discovered, and plugin modules.
/// </summary>
public sealed class ListModulesCommand : ICliCommand
{
    private readonly string _pluginsPath;
    private readonly string _modulesPath;

    public ListModulesCommand(string pluginsPath, string modulesPath)
    {
        _pluginsPath = pluginsPath;
        _modulesPath = modulesPath;
    }

    public bool CanHandle(string[] args) =>
        args.Length >= 1 &&
        (string.Equals(args[0], "--list-modules", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/list-modules", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        PrintInstalledModules();
        return Task.FromResult(0);
    }

    /// <summary>
    /// Prints all installed modules to the console, including standard, discovered, and plugin modules.
    /// This method is also used by the interactive CLI via <see cref="CliHostedService"/>.
    /// </summary>
    internal void PrintInstalledModules()
    {
        var prefs = ModulePreferences.Load();
        var standardInfos = ModuleSelector.GetStandardModuleInfos();

        Console.WriteLine("Standard modules:");
        foreach (var info in standardInfos)
        {
            var status = info.IsCore || prefs.IsModuleEnabled(info.Domain) ? "enabled" : "disabled";
            var coreTag = info.IsCore ? " (core)" : "";
            Console.WriteLine($"  - {info.Domain} [{status}]{coreTag}");
        }

        // Show modules from the modules/ folder
        var discoveredModules = ModuleSelector.DiscoverModulesFromFolder(_modulesPath);
        if (discoveredModules.Count > 0)
        {
            Console.WriteLine("Discovered modules (modules/ folder):");
            foreach (var info in discoveredModules)
            {
                var status = prefs.IsModuleEnabled(info.Domain) ? "enabled" : "disabled";
                Console.WriteLine($"  - {info.Domain} [{status}]  ({info.Source})");
            }
        }

        using var loaderServiceProvider = new ServiceCollection().BuildServiceProvider();
        var installedModules = InstalledModuleLoader.LoadModulesWithSources(_pluginsPath, loaderServiceProvider);
        var installedDlls = ModuleInstaller.ListInstalledModules(_pluginsPath);
        if (installedDlls.Count == 0)
        {
            Console.WriteLine("No installed plugin modules found.");
        }
        else
        {
            Console.WriteLine("Installed plugin modules:");
            foreach (var (module, dllPath) in installedModules)
            {
                Console.WriteLine($"  - {module.Domain}  ({Path.GetFileName(dllPath)})");
            }

            // Show any DLLs that didn't produce loadable modules
            var loadedDlls = installedModules.Select(m => Path.GetFileName(m.SourceDllPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var dll in installedDlls.Where(d => !loadedDlls.Contains(d)))
            {
                Console.WriteLine($"  - {dll}  (could not load)");
            }

            Console.WriteLine("Use '/unregister-module <domain or filename>' to remove.");
        }

        Console.WriteLine("\nUse '/configure-modules' or '--configure-modules' to change module selection.");
    }
}
