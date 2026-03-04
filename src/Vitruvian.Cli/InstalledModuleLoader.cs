using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using VitruvianAbstractions.Interfaces;

namespace VitruvianCli;

public static class InstalledModuleLoader
{
    public static IReadOnlyList<IVitruvianModule> LoadFromPluginsPath(string pluginsPath, IServiceProvider services)
    {
        if (!Directory.Exists(pluginsPath))
            return [];

        var modules = new List<IVitruvianModule>();
        foreach (var dllPath in Directory.EnumerateFiles(pluginsPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                modules.AddRange(CreateModulesFromAssembly(assembly, services));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to load plugin assembly '{dllPath}': {ex.Message}");
            }
        }

        return modules;
    }

    public static IReadOnlyList<IVitruvianModule> CreateModulesFromAssembly(Assembly assembly, IServiceProvider services)
    {
        var modules = new List<IVitruvianModule>();
        foreach (var moduleType in assembly
                     .GetExportedTypes()
                     .Where(static type => typeof(IVitruvianModule).IsAssignableFrom(type)
                                           && type is { IsAbstract: false, IsClass: true }))
        {
            try
            {
                if (ActivatorUtilities.CreateInstance(services, moduleType) is IVitruvianModule module)
                    modules.Add(module);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to activate module type '{moduleType.FullName}': {ex.Message}");
            }
        }

        return modules;
    }
}
