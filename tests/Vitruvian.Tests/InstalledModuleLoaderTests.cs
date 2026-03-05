using Microsoft.Extensions.DependencyInjection;
using VitruvianAbstractions.Interfaces;
using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class InstalledModuleLoaderTests
{
    [Fact]
    public void CreateModulesFromAssembly_WithVitruvianModuleType_CreatesModuleInstance()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var modules = InstalledModuleLoader.CreateModulesFromAssembly(typeof(InstalledModuleLoaderTestModule).Assembly, provider);

        Assert.Contains(modules, static module => module.GetType() == typeof(InstalledModuleLoaderTestModule));
    }

    [Fact]
    public void CreateModulesFromAssembly_WithICommandRunnerDependency_UsesFallbackRunner()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var modules = InstalledModuleLoader.CreateModulesFromAssembly(typeof(InstalledModuleLoaderCommandRunnerModule).Assembly, provider);

        Assert.Contains(modules, static module => module.GetType() == typeof(InstalledModuleLoaderCommandRunnerModule));
    }

    [Fact]
    public void LoadFromPluginsPath_WithMissingDirectory_ReturnsEmpty()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"vitruvian-missing-plugins-{Guid.NewGuid():N}");

        var modules = InstalledModuleLoader.LoadFromPluginsPath(pluginsPath, provider);

        Assert.Empty(modules);
    }

    [Fact]
    public void LoadFromPluginsPath_WithPluginAssembly_LoadsModule()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"vitruvian-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var sourceAssemblyPath = typeof(InstalledModuleLoaderTestModule).Assembly.Location;
            var destinationAssemblyPath = Path.Combine(pluginsPath, "InstalledModuleLoaderTestPlugin.dll");
            File.Copy(sourceAssemblyPath, destinationAssemblyPath, overwrite: true);

            var modules = InstalledModuleLoader.LoadFromPluginsPath(pluginsPath, provider);

            Assert.Contains(modules, static module => module.GetType() == typeof(InstalledModuleLoaderTestModule));
        }
        finally
        {
            if (Directory.Exists(pluginsPath))
                Directory.Delete(pluginsPath, recursive: true);
        }
    }

    [Fact]
    public void LoadModulesWithSources_WithMissingDirectory_ReturnsEmpty()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"vitruvian-missing-plugins-{Guid.NewGuid():N}");

        var modules = InstalledModuleLoader.LoadModulesWithSources(pluginsPath, provider);

        Assert.Empty(modules);
    }

    [Fact]
    public void LoadModulesWithSources_WithPluginAssembly_ReturnsDllPath()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"vitruvian-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var sourceAssemblyPath = typeof(InstalledModuleLoaderTestModule).Assembly.Location;
            var destinationAssemblyPath = Path.Combine(pluginsPath, "InstalledModuleLoaderTestPlugin.dll");
            File.Copy(sourceAssemblyPath, destinationAssemblyPath, overwrite: true);

            var modules = InstalledModuleLoader.LoadModulesWithSources(pluginsPath, provider);

            var match = Assert.Single(modules, m => m.Module.GetType() == typeof(InstalledModuleLoaderTestModule));
            Assert.Equal(Path.GetFullPath(destinationAssemblyPath), match.SourceDllPath);
        }
        finally
        {
            if (Directory.Exists(pluginsPath))
                Directory.Delete(pluginsPath, recursive: true);
        }
    }
}

public sealed class InstalledModuleLoaderTestModule : IVitruvianModule
{
    public string Domain => "installed-loader-test";
    public string Description => "Installed loader test module";

    public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
        => Task.FromResult("ok");
}

public sealed class InstalledModuleLoaderCommandRunnerModule : IVitruvianModule
{
    private readonly ICommandRunner _commandRunner;

    public InstalledModuleLoaderCommandRunnerModule(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public string Domain => "installed-loader-command-runner-test";
    public string Description => "Installed loader command runner test module";

    public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
        => Task.FromResult(_commandRunner.GetType().Name);
}
