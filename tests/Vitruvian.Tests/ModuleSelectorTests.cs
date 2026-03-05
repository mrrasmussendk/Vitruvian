using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class ModuleSelectorTests
{
    [Fact]
    public void GetStandardModuleInfos_ReturnsAllStandardModules()
    {
        var infos = ModuleSelector.GetStandardModuleInfos();

        Assert.Contains(infos, static m => m.Domain == "conversation");
        Assert.Contains(infos, static m => m.Domain == "file-operations");
        Assert.Contains(infos, static m => m.Domain == "shell-command");
        Assert.Contains(infos, static m => m.Domain == "web-search");
        Assert.Contains(infos, static m => m.Domain == "summarization");
    }

    [Fact]
    public void GetStandardModuleInfos_ConversationIsCore()
    {
        var infos = ModuleSelector.GetStandardModuleInfos();

        var conversation = Assert.Single(infos, static m => m.Domain == "conversation");
        Assert.True(conversation.IsCore);
    }

    [Fact]
    public void GetStandardModuleInfos_OptionalModulesAreNotCore()
    {
        var infos = ModuleSelector.GetStandardModuleInfos();

        var webSearch = Assert.Single(infos, static m => m.Domain == "web-search");
        Assert.False(webSearch.IsCore);
    }

    [Fact]
    public void GetStandardModuleInfos_AllModulesHaveSourceStandard()
    {
        var infos = ModuleSelector.GetStandardModuleInfos();

        Assert.All(infos, static m => Assert.Equal("standard", m.Source));
    }

    [Fact]
    public void DiscoverModulesFromFolder_WithMissingDirectory_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitruvian-modules-missing-{Guid.NewGuid():N}");

        var modules = ModuleSelector.DiscoverModulesFromFolder(path);

        Assert.Empty(modules);
    }

    [Fact]
    public void DiscoverModulesFromFolder_WithEmptyDirectory_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitruvian-modules-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var modules = ModuleSelector.DiscoverModulesFromFolder(path);

            Assert.Empty(modules);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void RunInteractiveSelection_DoneCommand_ReturnsPreferences()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("test-module", "A test module", "standard", [], IsCore: false)
        };
        var prefs = new ModulePreferences();
        var input = new StringReader("done\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        Assert.NotNull(result);
    }

    [Fact]
    public void RunInteractiveSelection_ToggleModule_FlipsEnabled()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("test-module", "A test module", "standard", [], IsCore: false)
        };
        var prefs = new ModulePreferences();
        // Toggle module 1 (disable), then done
        var input = new StringReader("1\ndone\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        Assert.False(result.IsModuleEnabled("test-module"));
    }

    [Fact]
    public void RunInteractiveSelection_ToggleModuleTwice_ReturnsToEnabled()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("test-module", "A test module", "standard", [], IsCore: false)
        };
        var prefs = new ModulePreferences();
        // Toggle twice (disable then re-enable), then done
        var input = new StringReader("1\n1\ndone\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        Assert.True(result.IsModuleEnabled("test-module"));
    }

    [Fact]
    public void RunInteractiveSelection_CoreModuleCannotBeDisabled()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("core-module", "A core module", "standard", [], IsCore: true)
        };
        var prefs = new ModulePreferences();
        // Try to toggle core module, then done
        var input = new StringReader("1\ndone\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        Assert.True(result.IsModuleEnabled("core-module"));
        Assert.Contains("core module", output.ToString());
    }

    [Fact]
    public void RunInteractiveSelection_AllCommand_EnablesAllModules()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("module-a", "Module A", "standard", [], IsCore: false),
            new("module-b", "Module B", "standard", [], IsCore: false)
        };
        var prefs = new ModulePreferences();
        prefs.SetModuleEnabled("module-a", false);
        prefs.SetModuleEnabled("module-b", false);
        var input = new StringReader("all\ndone\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        Assert.True(result.IsModuleEnabled("module-a"));
        Assert.True(result.IsModuleEnabled("module-b"));
    }

    [Fact]
    public void RunInteractiveSelection_NoneCommand_DisablesAllOptionalModules()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("core-module", "Core", "standard", [], IsCore: true),
            new("optional-module", "Optional", "standard", [], IsCore: false)
        };
        var prefs = new ModulePreferences();
        var input = new StringReader("none\ndone\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        Assert.True(result.IsModuleEnabled("core-module"));
        Assert.False(result.IsModuleEnabled("optional-module"));
    }

    [Fact]
    public void GetDomainFromType_WithoutCapabilityAttribute_ReturnsKebabCase()
    {
        // InstalledModuleLoaderTestModule doesn't have the attribute — falls back to kebab-case
        var domain = ModuleSelector.GetDomainFromType(typeof(InstalledModuleLoaderTestModule));

        Assert.Equal("installed-module-loader-test", domain);
    }

    [Fact]
    public void GetDomainFromType_WithoutAttribute_UsesKebabCaseTypeName()
    {
        var domain = ModuleSelector.GetDomainFromType(typeof(InstalledModuleLoaderTestModule));

        Assert.DoesNotContain(" ", domain);
        Assert.Equal(domain.ToLowerInvariant(), domain);
    }

    [Fact]
    public void RunInteractiveSelection_InvalidInput_IsIgnored()
    {
        var modules = new List<ModuleSelector.ModuleInfo>
        {
            new("test-module", "A test module", "standard", [], IsCore: false)
        };
        var prefs = new ModulePreferences();
        // Send invalid input, then done
        var input = new StringReader("invalid\n99\n\ndone\n");
        var output = new StringWriter();

        var result = ModuleSelector.RunInteractiveSelection(modules, prefs, input, output);

        // Module should still be enabled (default)
        Assert.True(result.IsModuleEnabled("test-module"));
    }
}
