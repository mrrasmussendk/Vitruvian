using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

namespace VitruvianCli;

public static class InstalledModuleLoader
{
    public static IReadOnlyList<IVitruvianModule> LoadFromPluginsPath(string pluginsPath, IServiceProvider services)
    {
        return LoadModulesWithSources(pluginsPath, services)
            .Select(static pair => pair.Module)
            .ToList();
    }

    /// <summary>
    /// Loads all plugin modules from the given directory and returns each module
    /// together with the absolute path of the source DLL it was loaded from.
    /// </summary>
    public static IReadOnlyList<(IVitruvianModule Module, string SourceDllPath)> LoadModulesWithSources(
        string pluginsPath, IServiceProvider services)
    {
        if (!Directory.Exists(pluginsPath))
            return [];

        var modules = new List<(IVitruvianModule, string)>();
        foreach (var dllPath in Directory.EnumerateFiles(pluginsPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fullPath = Path.GetFullPath(dllPath);
                var assembly = Assembly.LoadFrom(fullPath);
                foreach (var module in CreateModulesFromAssembly(assembly, services))
                {
                    modules.Add((module, fullPath));
                }
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
                WarnOnMissingApiKeys(moduleType);

                object? instance = RequiresCommandRunner(moduleType)
                    ? ActivatorUtilities.CreateInstance(
                        services,
                        moduleType,
                        services.GetService<ICommandRunner>() ?? new ProcessCommandRunner())
                    : ActivatorUtilities.CreateInstance(services, moduleType);

                if (instance is IVitruvianModule module)
                    modules.Add(module);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to activate module type '{moduleType.FullName}': {ex.Message}");
            }
        }

        return modules;
    }

    private static bool RequiresCommandRunner(Type moduleType)
    {
        return moduleType
            .GetConstructors()
            .SelectMany(static ctor => ctor.GetParameters())
            .Any(static parameter => parameter.ParameterType == typeof(ICommandRunner));
    }

    /// <summary>
    /// Inspects the <see cref="RequiresApiKeyAttribute"/> declarations on
    /// <paramref name="moduleType"/> and emits a console warning for each
    /// environment variable that is not set.
    /// </summary>
    internal static IReadOnlyList<string> WarnOnMissingApiKeys(Type moduleType)
    {
        var missing = GetMissingApiKeys(moduleType);
        foreach (var envVar in missing)
        {
            Console.WriteLine(
                $"[WARN] Module '{moduleType.Name}' requires API key '{envVar}' but the environment variable is not set. " +
                "Set it in .env.Vitruvian or as an environment variable.");
        }

        return missing;
    }

    /// <summary>
    /// Returns the list of environment variable names declared via
    /// <see cref="RequiresApiKeyAttribute"/> on <paramref name="moduleType"/>
    /// that are currently unset or empty.
    /// </summary>
    internal static IReadOnlyList<string> GetMissingApiKeys(Type moduleType)
    {
        return moduleType
            .GetCustomAttributes<RequiresApiKeyAttribute>(inherit: true)
            .Select(static attr => attr.EnvironmentVariable.Trim())
            .Where(static envVar => envVar.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(static envVar => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envVar)))
            .ToArray();
    }
}
