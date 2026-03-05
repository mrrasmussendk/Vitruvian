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
    /// are treated as enabled by default.
    /// </summary>
    [JsonPropertyName("enabledModules")]
    public Dictionary<string, bool> EnabledModules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
    /// Modules not explicitly configured are enabled by default.
    /// </summary>
    public bool IsModuleEnabled(string domain)
        => !EnabledModules.TryGetValue(domain, out var enabled) || enabled;

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
