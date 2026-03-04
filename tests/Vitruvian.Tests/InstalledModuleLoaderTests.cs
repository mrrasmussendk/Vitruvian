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
    public void LoadFromPluginsPath_WithMissingDirectory_ReturnsEmpty()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"vitruvian-missing-plugins-{Guid.NewGuid():N}");

        var modules = InstalledModuleLoader.LoadFromPluginsPath(pluginsPath, provider);

        Assert.Empty(modules);
    }
}

public sealed class InstalledModuleLoaderTestModule : IVitruvianModule
{
    public string Domain => "installed-loader-test";
    public string Description => "Installed loader test module";

    public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
        => Task.FromResult("ok");
}
