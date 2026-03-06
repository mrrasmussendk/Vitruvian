using Xunit;
using VitruvianPluginSdk;

namespace VitruvianTests;

public sealed class ModuleSkillLoaderTests
{
    [Fact]
    public void LoadMarkdownSkill_WhenFileExistsInAssemblyDirectory_LoadsContent()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ModuleSkillLoaderTests).Assembly.Location)!;
        var fileName = $"skill-{Guid.NewGuid():N}.md";
        var skillPath = Path.Combine(assemblyDirectory, fileName);

        File.WriteAllText(skillPath, "# Local Skill\nUse this.");

        try
        {
            var loaded = ModuleSkillLoader.LoadMarkdownSkill(typeof(ModuleSkillLoaderTests), fileName, "fallback");
            Assert.Equal("# Local Skill\nUse this.", loaded);
        }
        finally
        {
            if (File.Exists(skillPath))
                File.Delete(skillPath);
        }
    }

    [Fact]
    public void LoadMarkdownSkill_WhenFileMissing_ReturnsFallback()
    {
        var loaded = ModuleSkillLoader.LoadMarkdownSkill(typeof(ModuleSkillLoaderTests), $"missing-{Guid.NewGuid():N}.md", "fallback");
        Assert.Equal("fallback", loaded);
    }

    [Fact]
    public void LoadMarkdownSkill_WhenEmbeddedResourceExists_LoadsContent()
    {
        var loaded = ModuleSkillLoader.LoadMarkdownSkill(typeof(ModuleSkillLoaderTests), "module-skill.md", "fallback");
        Assert.Equal("# Embedded Skill\nLoaded from manifest resource.", loaded.ReplaceLineEndings("\n").TrimEnd());
    }
}
