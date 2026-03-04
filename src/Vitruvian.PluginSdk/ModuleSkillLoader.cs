namespace VitruvianPluginSdk;

/// <summary>
/// Loads local markdown skills for modules with sensible defaults.
/// </summary>
public static class ModuleSkillLoader
{
    /// <summary>
    /// Loads a markdown skill file from common local locations for a module.
    /// </summary>
    /// <param name="moduleType">Module type used to resolve assembly-relative paths.</param>
    /// <param name="fileName">Skill markdown file name.</param>
    /// <param name="fallback">Fallback content when no local file exists.</param>
    /// <returns>Loaded markdown content, or <paramref name="fallback"/> if not found.</returns>
    public static string LoadMarkdownSkill(Type moduleType, string fileName, string fallback)
    {
        if (moduleType is null)
            throw new ArgumentNullException(nameof(moduleType));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));

        var assemblyDirectory = Path.GetDirectoryName(moduleType.Assembly.Location);
        var searchRoots = new[]
        {
            assemblyDirectory,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in searchRoots.Where(static p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal))
        {
            var directPath = Path.Combine(root!, fileName);
            if (File.Exists(directPath))
                return File.ReadAllText(directPath);

            var skillsPath = Path.Combine(root!, "skills", fileName);
            if (File.Exists(skillsPath))
                return File.ReadAllText(skillsPath);
        }

        return fallback;
    }
}
