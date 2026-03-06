using System.Text.Json;
using System.Text.Json.Serialization;

namespace VitruvianCli;

/// <summary>
/// Manages user preferences for which modules are enabled or disabled.
/// Preferences are persisted to a <c>vitruvian-modules.json</c> file so
/// that the user's choices survive across restarts.
/// </summary>
public sealed class ModulePreferences
{
    private const string FileName = "vitruvian-modules.json";

    /// <summary>
    /// Map of module domain → enabled flag. Modules not present in this map
    /// are treated as disabled by default (opt-in).
    /// </summary>
    [JsonPropertyName("enabledModules")]
    public Dictionary<string, bool> EnabledModules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of module domains that are considered "built-in" and enabled by default.
    /// Discovered modules from the modules/ folder require explicit opt-in.
    /// </summary>
    [JsonPropertyName("builtInModules")]
    public HashSet<string> BuiltInModules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the default file path for the preferences file, next to the application.
    /// </summary>
    public static string GetDefaultPath() => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>
    /// Loads preferences from the given path (or the default path).
    /// Returns an empty <see cref="ModulePreferences"/> if the file does not exist.
    /// </summary>
    public static ModulePreferences Load(string? path = null)
    {
        var filePath = path ?? GetDefaultPath();
        if (!File.Exists(filePath))
            return new ModulePreferences();

        try
        {
            var json = File.ReadAllText(filePath);
            var prefs = JsonSerializer.Deserialize<ModulePreferences>(json, JsonOptions);
            if (prefs is not null)
            {
                // Re-create with case-insensitive comparer
                var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in prefs.EnabledModules)
                    normalized[kvp.Key] = kvp.Value;
                prefs.EnabledModules = normalized;

                // Ensure BuiltInModules uses case-insensitive comparer
                var builtInNormalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var module in prefs.BuiltInModules)
                    builtInNormalized.Add(module);
                prefs.BuiltInModules = builtInNormalized;
            }
            return prefs ?? new ModulePreferences();
        }
        catch (JsonException)
        {
            return new ModulePreferences();
        }
    }

    /// <summary>
    /// Saves the current preferences to the given path (or the default path).
    /// </summary>
    public void Save(string? path = null)
    {
        var filePath = path ?? GetDefaultPath();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Returns <c>true</c> if the module with the given domain is enabled.
    /// Built-in modules are enabled by default. Discovered modules require explicit opt-in.
    /// </summary>
    public bool IsModuleEnabled(string domain)
    {
        // Check explicit preference first
        if (EnabledModules.TryGetValue(domain, out var enabled))
            return enabled;

        // For modules not explicitly configured, check if they're built-in
        return BuiltInModules.Contains(domain);
    }

    /// <summary>
    /// Sets whether the module with the given domain should be enabled or disabled.
    /// </summary>
    public void SetModuleEnabled(string domain, bool enabled)
        => EnabledModules[domain] = enabled;

    /// <summary>
    /// Returns <c>true</c> if the preferences file has never been saved
    /// (i.e. no modules have been explicitly configured).
    /// </summary>
    public bool IsEmpty => EnabledModules.Count == 0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
