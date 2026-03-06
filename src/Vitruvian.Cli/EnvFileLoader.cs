namespace VitruvianCli;

/// <summary>
/// Loads environment variables from a <c>.env.Vitruvian</c> file so the host
/// "just works" after running <c>scripts/install.sh</c> or <c>scripts/install.ps1</c>.
/// Supports three line formats:
/// <list type="bullet">
///   <item><c>KEY=VALUE</c> (standard .env)</item>
///   <item><c>export KEY=VALUE</c> (bash / install.sh)</item>
///   <item><c>$env:KEY='VALUE'</c> (PowerShell / install.ps1)</item>
/// </list>
/// Existing environment variables are not overwritten by default, allowing callers
/// to override individual values via the shell when needed. If <c>VITRUVIAN_PROFILE</c>
/// is set, an additional <c>.env.Vitruvian&lt;profile&gt;</c> file is loaded after the base file.
/// </summary>
public static class EnvFileLoader
{
    private const string FileName = ".env.Vitruvian";
    private const string LegacyProfileFileNamePrefix = ".env.Vitruvian";
    private const string ProfileVariableName = "VITRUVIAN_PROFILE";

    /// <summary>
    /// Searches for <c>.env.Vitruvian</c> starting from <paramref name="startDirectory"/>
    /// (defaults to <see cref="Directory.GetCurrentDirectory"/>) and walking up to
    /// ancestor directories. If the file is found, each recognised line is set as an
    /// environment variable for the current process.
    /// </summary>
    public static void Load(string? startDirectory = null, bool overwriteExisting = false)
    {
        var searchDirectories = new[]
        {
            startDirectory ?? Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };
        var path = FindFile(searchDirectories);
        if (path is null)
            return;

        // Tracks variables set during this Load invocation so base and profile files
        // can layer values while still respecting externally supplied environment values.
        var keysLoadedByLoader = new HashSet<string>(StringComparer.Ordinal);
        LoadFile(path, overwriteExisting, keysLoadedByLoader);

        var profile = Environment.GetEnvironmentVariable(ProfileVariableName)?.Trim();
        if (string.IsNullOrWhiteSpace(profile))
            return;

        var profilePath = FindFile(searchDirectories, $"{FileName}.{profile}")
                          ?? FindFile(searchDirectories, $"{LegacyProfileFileNamePrefix}{profile}");
        if (profilePath is null || string.Equals(profilePath, path, StringComparison.OrdinalIgnoreCase))
            return;

        LoadFile(profilePath, overwriteExisting, keysLoadedByLoader);
    }

    private static void LoadFile(string path, bool overwriteExisting, ISet<string> keysLoadedByLoader)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var (key, value) = ParseLine(rawLine);
            if (key is null)
                continue;

            if (ShouldSetVariable(key, overwriteExisting, keysLoadedByLoader))
            {
                Environment.SetEnvironmentVariable(key, value);
                keysLoadedByLoader.Add(key);
            }
        }
    }

    private static bool ShouldSetVariable(string key, bool overwriteExisting, ISet<string> keysLoadedByLoader)
    {
        if (overwriteExisting)
            return true;

        if (keysLoadedByLoader.Contains(key))
            return true;

        return Environment.GetEnvironmentVariable(key) is null;
    }

    /// <summary>
    /// Persists <paramref name="key"/>=<paramref name="value"/> to the <c>.env.Vitruvian</c>
    /// file.  If the file already contains an entry for <paramref name="key"/> it is updated
    /// in-place; otherwise a new line is appended.  When no existing file is found, one is
    /// created in <paramref name="fallbackDirectory"/> (defaults to <see cref="AppContext.BaseDirectory"/>).
    /// </summary>
    public static void PersistSecret(string key, string value, string? fallbackDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var searchDirs = new[] { fallbackDirectory ?? Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        var path = FindFile(searchDirs) ?? Path.Combine(AppContext.BaseDirectory, FileName);

        // Read existing lines (or start with an empty list for a new file).
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var (parsedKey, _) = ParseLine(lines[i]);
            if (parsedKey is not null && string.Equals(parsedKey, key, StringComparison.Ordinal))
            {
                lines[i] = $"{key}={value}";
                replaced = true;
                break;
            }
        }

        if (!replaced)
            lines.Add($"{key}={value}");

        File.WriteAllLines(path, lines);
    }

    public static string? FindFile(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        return FindFileInAncestors(startDirectory);
    }

    public static string? FindFile(IEnumerable<string?> startDirectories)
    {
        return FindFile(startDirectories, FileName);
    }

    private static string? FindFile(IEnumerable<string?> startDirectories, string fileName)
    {
        ArgumentNullException.ThrowIfNull(startDirectories);
        foreach (var startDirectory in startDirectories)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
                continue;

            var path = FindFileInAncestors(startDirectory, fileName);
            if (path is not null)
                return path;
        }

        return null;
    }

    private static string? FindFileInAncestors(string startDirectory)
        => FindFileInAncestors(startDirectory, FileName);

    private static string? FindFileInAncestors(string startDirectory, string fileName)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    public static (string? Key, string? Value) ParseLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return (null, null);

        // PowerShell format: $env:KEY='VALUE'
        if (trimmed.StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed["$env:".Length..];
            var eqIndex = rest.IndexOf('=');
            if (eqIndex <= 0)
                return (null, null);

            var key = rest[..eqIndex];
            var value = Unquote(rest[(eqIndex + 1)..]);
            return (key, value);
        }

        // Bash format: export KEY=VALUE
        if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["export ".Length..].TrimStart();

        // Standard: KEY=VALUE
        var idx = trimmed.IndexOf('=');
        if (idx <= 0)
            return (null, null);

        return (trimmed[..idx], Unquote(trimmed[(idx + 1)..]));
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[^1] == '\'') ||
             (value[0] == '"' && value[^1] == '"')))
            return value[1..^1];

        return value;
    }
}
