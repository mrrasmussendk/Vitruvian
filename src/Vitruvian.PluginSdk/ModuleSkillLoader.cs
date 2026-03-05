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

        var assemblyDirectory = Path.GetDirectoryName(moduleType.Assembly.Location) ?? string.Empty;
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

        var embeddedSkill = TryLoadEmbeddedSkill(moduleType, fileName);
        if (embeddedSkill is not null)
            return embeddedSkill;

        return fallback;
    }

    private static string? TryLoadEmbeddedSkill(Type moduleType, string fileName)
    {
        var assembly = moduleType.Assembly;
        var normalizedFileName = fileName.Replace('\\', '/').TrimStart('/');
        var dottedFileName = normalizedFileName.Replace('/', '.');

        var candidateNames = new[]
        {
            fileName,
            normalizedFileName,
            dottedFileName,
            string.IsNullOrWhiteSpace(moduleType.Namespace) ? null : $"{moduleType.Namespace}.{dottedFileName}"
        };

        foreach (var candidateName in candidateNames.Where(static n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(candidateName!);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith($".{dottedFileName}", StringComparison.OrdinalIgnoreCase)
                && !resourceName.Equals(dottedFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return null;
    }
}
