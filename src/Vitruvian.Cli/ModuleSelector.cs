using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;
using VitruvianStandardModules;

namespace VitruvianCli;

/// <summary>
/// Discovers modules from the <c>modules/</c> folder and manages interactive selection
/// so users can choose which modules to enable or disable at startup.
/// </summary>
public static class ModuleSelector
{
    /// <summary>
    /// Describes a discovered module with its metadata.
    /// </summary>
    /// <param name="Domain">Unique module identifier.</param>
    /// <param name="Description">Human-readable description of the module.</param>
    /// <param name="Source">Where the module comes from (e.g. "standard" or a DLL file name).</param>
    /// <param name="RequiredApiKeys">Environment variables required by the module.</param>
    /// <param name="IsCore">If <c>true</c>, the module cannot be disabled.</param>
    public sealed record ModuleInfo(
        string Domain,
        string Description,
        string Source,
        IReadOnlyList<string> RequiredApiKeys,
        bool IsCore = false);

    /// <summary>
    /// Returns metadata about all built-in standard modules.
    /// </summary>
    public static IReadOnlyList<ModuleInfo> GetStandardModuleInfos()
    {
        return
        [
            new ModuleInfo("conversation", "Answer general questions and hold conversations",
                "standard", [], IsCore: true),
            new ModuleInfo("file-operations", "Read, write, and list files",
                "standard", GetApiKeysForType(typeof(FileOperationsModule))),
            new ModuleInfo("shell-command", "Execute shell commands",
                "standard", GetApiKeysForType(typeof(ShellCommandModule))),
            new ModuleInfo("web-search", "Search the web for information",
                "standard", GetApiKeysForType(typeof(WebSearchModule))),
            new ModuleInfo("summarization", "Summarize text and documents",
                "standard", GetApiKeysForType(typeof(SummarizationModule))),
            new ModuleInfo("gmail", "Read and compose Gmail messages",
                "standard", GetApiKeysForType(typeof(GmailModule)))
        ];
    }

    /// <summary>
    /// Discovers module DLLs in the given <paramref name="modulesPath"/> directory
    /// and returns metadata about each discovered module without loading them into the runtime.
    /// </summary>
    public static IReadOnlyList<ModuleInfo> DiscoverModulesFromFolder(string modulesPath)
    {
        if (!Directory.Exists(modulesPath))
            return [];

        var modules = new List<ModuleInfo>();
        foreach (var dllPath in Directory.EnumerateFiles(modulesPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var assembly = Assembly.LoadFrom(Path.GetFullPath(dllPath));
                foreach (var moduleType in assembly
                             .GetExportedTypes()
                             .Where(static type => typeof(IVitruvianModule).IsAssignableFrom(type)
                                                   && type is { IsAbstract: false, IsClass: true }))
                {
                    var domain = GetDomainFromType(moduleType);
                    var description = GetDescriptionFromType(moduleType);
                    var apiKeys = GetApiKeysForType(moduleType);
                    var fileName = Path.GetFileName(dllPath);
                    modules.Add(new ModuleInfo(domain, description, fileName, apiKeys));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to inspect module assembly '{Path.GetFileName(dllPath)}': {ex.Message}");
            }
        }

        return modules;
    }

    /// <summary>
    /// Runs an interactive console prompt that lets the user toggle modules on or off.
    /// Returns the updated <see cref="ModulePreferences"/>.
    /// </summary>
    public static ModulePreferences RunInteractiveSelection(
        IReadOnlyList<ModuleInfo> availableModules,
        ModulePreferences currentPreferences,
        TextReader? input = null,
        TextWriter? output = null)
    {
        var reader = input ?? Console.In;
        var writer = output ?? Console.Out;

        writer.WriteLine();
        writer.WriteLine("=== Module Configuration ===");
        writer.WriteLine("Select which modules to enable. Enter a number to toggle, or a command:");
        writer.WriteLine("  'all'  — enable all modules");
        writer.WriteLine("  'none' — disable all optional modules");
        writer.WriteLine("  'done' — save and continue");
        writer.WriteLine();

        while (true)
        {
            for (var i = 0; i < availableModules.Count; i++)
            {
                var mod = availableModules[i];
                var isEnabled = mod.IsCore || currentPreferences.IsModuleEnabled(mod.Domain);
                var status = isEnabled ? "[✓]" : "[ ]";
                var coreTag = mod.IsCore ? " (core)" : "";
                var sourceTag = mod.Source != "standard" ? $" [{mod.Source}]" : "";
                var apiKeyInfo = mod.RequiredApiKeys.Count > 0
                    ? $" (requires: {string.Join(", ", mod.RequiredApiKeys)})"
                    : "";
                writer.WriteLine($"  {i + 1}. {status} {mod.Domain} — {mod.Description}{coreTag}{sourceTag}{apiKeyInfo}");
            }

            writer.Write("\nToggle (number), 'all', 'none', or 'done': ");
            var line = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            if (string.Equals(line, "done", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.Equals(line, "all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var mod in availableModules)
                    currentPreferences.SetModuleEnabled(mod.Domain, true);
                writer.WriteLine("  All modules enabled.\n");
                continue;
            }

            if (string.Equals(line, "none", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var mod in availableModules.Where(static m => !m.IsCore))
                    currentPreferences.SetModuleEnabled(mod.Domain, false);
                writer.WriteLine("  All optional modules disabled.\n");
                continue;
            }

            if (int.TryParse(line, out var index) && index >= 1 && index <= availableModules.Count)
            {
                var mod = availableModules[index - 1];
                if (mod.IsCore)
                {
                    writer.WriteLine($"  '{mod.Domain}' is a core module and cannot be disabled.\n");
                    continue;
                }

                var wasEnabled = currentPreferences.IsModuleEnabled(mod.Domain);
                currentPreferences.SetModuleEnabled(mod.Domain, !wasEnabled);
                writer.WriteLine($"  {mod.Domain}: {(wasEnabled ? "disabled" : "enabled")}\n");
            }
            else
            {
                writer.WriteLine("  Invalid input. Enter a number, 'all', 'none', or 'done'.\n");
            }
        }

        return currentPreferences;
    }

    /// <summary>
    /// Loads module DLLs from the <paramref name="modulesPath"/> folder, instantiating only
    /// modules whose domain is enabled in the given <paramref name="preferences"/>.
    /// </summary>
    public static IReadOnlyList<(IVitruvianModule Module, string SourceDllPath)> LoadEnabledModules(
        string modulesPath, ModulePreferences preferences, IServiceProvider services)
    {
        if (!Directory.Exists(modulesPath))
            return [];

        var modules = new List<(IVitruvianModule, string)>();
        foreach (var dllPath in Directory.EnumerateFiles(modulesPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fullPath = Path.GetFullPath(dllPath);
                var assembly = Assembly.LoadFrom(fullPath);
                foreach (var moduleType in assembly
                             .GetExportedTypes()
                             .Where(static type => typeof(IVitruvianModule).IsAssignableFrom(type)
                                                   && type is { IsAbstract: false, IsClass: true }))
                {
                    var domain = GetDomainFromType(moduleType);
                    if (!preferences.IsModuleEnabled(domain))
                    {
                        Console.WriteLine($"[INFO] Module '{domain}' is disabled by preferences — skipping.");
                        continue;
                    }

                    InstalledModuleLoader.WarnOnMissingApiKeys(moduleType);
                    if (ActivatorUtilities.CreateInstance(services, moduleType) is IVitruvianModule module)
                        modules.Add((module, fullPath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to load module assembly '{Path.GetFileName(dllPath)}': {ex.Message}");
            }
        }

        return modules;
    }

    /// <summary>
    /// Gets the domain string from a module type by instantiating it temporarily or reading attributes.
    /// Falls back to a normalized type name if instantiation fails.
    /// </summary>
    internal static string GetDomainFromType(Type moduleType)
    {
        var cap = moduleType.GetCustomAttribute<VitruvianCapabilityAttribute>();
        if (cap is not null)
            return cap.Domain;

        // Fall back to a convention-based name
        var name = moduleType.Name;
        if (name.EndsWith("Module", StringComparison.Ordinal))
            name = name[..^"Module".Length];

        return ToKebabCase(name);
    }

    /// <summary>
    /// Gets the description from a module type via attributes or falls back to type name.
    /// </summary>
    internal static string GetDescriptionFromType(Type moduleType)
    {
        var cap = moduleType.GetCustomAttribute<VitruvianCapabilityAttribute>();
        if (cap is not null && !string.IsNullOrWhiteSpace(cap.Domain))
        {
            // Use domain as a fallback description
        }

        return $"{moduleType.Name} module";
    }

    internal static IReadOnlyList<string> GetApiKeysForType(Type moduleType)
        => InstalledModuleLoader.GetMissingApiKeys(moduleType);

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    result.Append('-');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
