using VitruvianCli;
using Xunit;

namespace VitruvianTests;

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

    [Fact]
    public void ScaffoldNewModule_GeneratesVitruvianManifestAndSdkReferences()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"vitruvian-scaffold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var moduleName = "ExampleModule";
            var scaffoldMessage = ModuleInstaller.ScaffoldNewModule(moduleName, tempRoot);
            var moduleDirectory = Path.Combine(tempRoot, moduleName);
            var projectPath = Path.Combine(moduleDirectory, $"{moduleName}.csproj");
            var manifestPath = Path.Combine(moduleDirectory, "vitruvian-manifest.json");
            var readmePath = Path.Combine(moduleDirectory, "README.md");

            Assert.Contains("Created module scaffold", scaffoldMessage);
            Assert.True(File.Exists(projectPath));
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(readmePath));

            var projectContents = File.ReadAllText(projectPath);
            Assert.Contains("<TargetFramework>net", projectContents);
            Assert.DoesNotContain("<TargetFramework>net10.0</TargetFramework>", projectContents);
            Assert.Contains("<PackageReference Include=\"Vitruvian.Abstractions\" Version=\"0.*\" />", projectContents);
            Assert.Contains("<PackageReference Include=\"Vitruvian.PluginSdk\" Version=\"0.*\" />", projectContents);

            var readmeContents = File.ReadAllText(readmePath);
            Assert.Contains("vitruvian-manifest.json", readmeContents);
            Assert.Contains("bin/Debug/net", readmeContents);
            Assert.DoesNotContain("bin/Debug/net10.0", readmeContents);
            Assert.DoesNotContain("Vitruvian-manifest.json", readmeContents);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InstallWithResultAsync_WithNonModuleAssembly_ReturnsActionableCompatibilityMessage()
    {
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"vitruvian-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var result = await ModuleInstaller.InstallWithResultAsync(
                typeof(ModuleInstaller).Assembly.Location,
                pluginsPath,
                allowUnsigned: true);

            Assert.False(result.Success);
            Assert.Contains("compatible Vitruvian module assembly", result.Message);
            Assert.Contains("IVitruvianModule", result.Message);
            Assert.DoesNotContain("compatible UtilityAI module assembly", result.Message);
        }
        finally
        {
            if (Directory.Exists(pluginsPath))
                Directory.Delete(pluginsPath, recursive: true);
        }
    }
}
