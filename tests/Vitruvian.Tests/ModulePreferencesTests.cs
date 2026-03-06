using System.Text.Json;
using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class ModulePreferencesTests
{
    [Fact]
    public void IsModuleEnabled_WithNoPreferences_ReturnsFalseForDiscoveredModules()
    {
        var prefs = new ModulePreferences();

        // Discovered modules are disabled by default (opt-in)
        Assert.False(prefs.IsModuleEnabled("conversation"));
        Assert.False(prefs.IsModuleEnabled("file-operations"));
        Assert.False(prefs.IsModuleEnabled("unknown-module"));
    }

    [Fact]
    public void IsModuleEnabled_BuiltInModules_AreEnabledByDefault()
    {
        var prefs = new ModulePreferences();
        prefs.BuiltInModules.Add("conversation");
        prefs.BuiltInModules.Add("file-operations");

        Assert.True(prefs.IsModuleEnabled("conversation"));
        Assert.True(prefs.IsModuleEnabled("file-operations"));
        Assert.False(prefs.IsModuleEnabled("unknown-module"));
    }

    [Fact]
    public void SetModuleEnabled_DisablesModule()
    {
        var prefs = new ModulePreferences();
        prefs.BuiltInModules.Add("conversation");

        prefs.SetModuleEnabled("gmail", false);

        Assert.False(prefs.IsModuleEnabled("gmail"));
        Assert.True(prefs.IsModuleEnabled("conversation"));
    }

    [Fact]
    public void SetModuleEnabled_ReEnablesModule()
    {
        var prefs = new ModulePreferences();
        prefs.SetModuleEnabled("gmail", false);

        prefs.SetModuleEnabled("gmail", true);

        Assert.True(prefs.IsModuleEnabled("gmail"));
    }

    [Fact]
    public void IsModuleEnabled_IsCaseInsensitive()
    {
        var prefs = new ModulePreferences();
        prefs.SetModuleEnabled("Gmail", false);

        Assert.False(prefs.IsModuleEnabled("gmail"));
        Assert.False(prefs.IsModuleEnabled("GMAIL"));
    }

    [Fact]
    public void IsEmpty_WhenNoModulesConfigured_ReturnsTrue()
    {
        var prefs = new ModulePreferences();

        Assert.True(prefs.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenModulesConfigured_ReturnsFalse()
    {
        var prefs = new ModulePreferences();
        prefs.SetModuleEnabled("gmail", false);

        Assert.False(prefs.IsEmpty);
    }

    [Fact]
    public void Save_And_Load_RoundTrips()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vitruvian-prefs-{Guid.NewGuid():N}.json");
        try
        {
            var prefs = new ModulePreferences();
            prefs.BuiltInModules.Add("conversation");
            prefs.SetModuleEnabled("gmail", false);
            prefs.SetModuleEnabled("web-search", true);
            prefs.SetModuleEnabled("shell-command", false);

            prefs.Save(tempFile);

            var loaded = ModulePreferences.Load(tempFile);

            Assert.False(loaded.IsModuleEnabled("gmail"));
            Assert.True(loaded.IsModuleEnabled("web-search"));
            Assert.False(loaded.IsModuleEnabled("shell-command"));
            Assert.True(loaded.IsModuleEnabled("conversation")); // Built-in module
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithMissingFile_ReturnsEmptyPreferences()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"vitruvian-prefs-missing-{Guid.NewGuid():N}.json");

        var loaded = ModulePreferences.Load(nonExistentPath);

        Assert.True(loaded.IsEmpty);
        Assert.False(loaded.IsModuleEnabled("any-module")); // Opt-in by default
    }

    [Fact]
    public void Load_WithInvalidJson_ReturnsEmptyPreferences()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vitruvian-prefs-invalid-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ invalid json }}}");

            var loaded = ModulePreferences.Load(tempFile);

            Assert.True(loaded.IsEmpty);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Save_CreatesValidJsonFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vitruvian-prefs-json-{Guid.NewGuid():N}.json");
        try
        {
            var prefs = new ModulePreferences();
            prefs.SetModuleEnabled("gmail", false);

            prefs.Save(tempFile);

            var json = File.ReadAllText(tempFile);
            var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("enabledModules", out var modulesElement));
            Assert.Equal(JsonValueKind.Object, modulesElement.ValueKind);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
