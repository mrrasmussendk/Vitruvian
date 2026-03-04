using Xunit;
using System.Reflection;
using VitruvianAbstractions.Interfaces;
using VitruvianCli;
using VitruvianPluginSdk.Attributes;

namespace VitruvianTests;

public sealed class ModuleInstallerApiKeyAttributeTests
{
    [Fact]
    public void InstallerDiscoversRequiredApiKeysFromModuleAttributes()
    {
        var method = typeof(ModuleInstaller).GetMethod("GetRequiredApiKeysFromAssembly", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [typeof(ModuleInstallerApiKeyAttributeTests).Assembly.Location]);
        var keys = Assert.IsAssignableFrom<IReadOnlyList<string>>(result);

        Assert.Contains("TEST_API_KEY_1", keys);
        Assert.Contains("TEST_API_KEY_2", keys);
    }

    [RequiresApiKey("TEST_API_KEY_1")]
    [RequiresApiKey("TEST_API_KEY_2")]
    [RequiresApiKey("TEST_API_KEY_1")]
    public sealed class ApiKeyTestModule : IVitruvianModule
    {
        public string Domain => "test-api-key-module";
        public string Description => "Test module";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct) => Task.FromResult("ok");
    }
}
