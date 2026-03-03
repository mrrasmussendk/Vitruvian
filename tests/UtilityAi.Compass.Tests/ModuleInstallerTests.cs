using UtilityAi.Compass.Cli;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class ModuleInstallerTests
{
    [Fact]
    public void TryParseInstallCommand_WithExactCommand_ParsesModuleSpec()
    {
        var ok = ModuleInstaller.TryParseInstallCommand("/install-module Example.Module@1.0.0", out var moduleSpec, out var allowUnsigned);

        Assert.True(ok);
        Assert.Equal("Example.Module@1.0.0", moduleSpec);
        Assert.False(allowUnsigned);
    }

    [Fact]
    public void TryParseInstallCommand_WithCommandPrefixOnly_ReturnsFalse()
    {
        var ok = ModuleInstaller.TryParseInstallCommand("/install-modulex Example.Module@1.0.0", out var moduleSpec, out var allowUnsigned);

        Assert.False(ok);
        Assert.Equal(string.Empty, moduleSpec);
        Assert.False(allowUnsigned);
    }

    [Fact]
    public void TryParseNewModuleCommand_WithExactCommand_ParsesModuleName()
    {
        var ok = ModuleInstaller.TryParseNewModuleCommand("/new-module ExampleModule", out var moduleName, out var outputPath);

        Assert.True(ok);
        Assert.Equal("ExampleModule", moduleName);
        Assert.Equal(Directory.GetCurrentDirectory(), outputPath);
    }

    [Fact]
    public void TryParseNewModuleCommand_WithCommandPrefixOnly_ReturnsFalse()
    {
        var ok = ModuleInstaller.TryParseNewModuleCommand("/new-modulex ExampleModule", out var moduleName, out var outputPath);

        Assert.False(ok);
        Assert.Equal(string.Empty, moduleName);
        Assert.Equal(Directory.GetCurrentDirectory(), outputPath);
    }
}
