using VitruvianAbstractions.Interfaces;
using VitruvianCli;
using VitruvianPluginSdk.Attributes;
using Xunit;

namespace VitruvianTests;

public sealed class InstalledModuleLoaderApiKeyTests : IDisposable
{
    private readonly string? _originalValue;
    private const string TestEnvVar = "VITRUVIAN_TEST_LOADER_KEY_ABC123";

    public InstalledModuleLoaderApiKeyTests()
    {
        _originalValue = Environment.GetEnvironmentVariable(TestEnvVar);
    }

    public void Dispose()
    {
        // Restore the original value (or clear it).
        Environment.SetEnvironmentVariable(TestEnvVar, _originalValue);
    }

    [Fact]
    public void GetMissingApiKeys_ReturnsMissingKeys_WhenEnvVarNotSet()
    {
        Environment.SetEnvironmentVariable(TestEnvVar, null);

        var missing = InstalledModuleLoader.GetMissingApiKeys(typeof(ApiKeyLoaderTestModule));

        Assert.Contains(TestEnvVar, missing);
    }

    [Fact]
    public void GetMissingApiKeys_ReturnsEmpty_WhenEnvVarIsSet()
    {
        Environment.SetEnvironmentVariable(TestEnvVar, "some-secret-value");

        var missing = InstalledModuleLoader.GetMissingApiKeys(typeof(ApiKeyLoaderTestModule));

        Assert.DoesNotContain(TestEnvVar, missing);
    }

    [Fact]
    public void WarnOnMissingApiKeys_ReturnsListOfMissing()
    {
        Environment.SetEnvironmentVariable(TestEnvVar, null);

        var missing = InstalledModuleLoader.WarnOnMissingApiKeys(typeof(ApiKeyLoaderTestModule));

        Assert.Contains(TestEnvVar, missing);
    }

    [Fact]
    public void GetMissingApiKeys_ReturnsEmpty_ForModuleWithoutAttribute()
    {
        var missing = InstalledModuleLoader.GetMissingApiKeys(typeof(NoApiKeyModule));

        Assert.Empty(missing);
    }

    [RequiresApiKey(TestEnvVar)]
    public sealed class ApiKeyLoaderTestModule : IVitruvianModule
    {
        public string Domain => "api-key-loader-test";
        public string Description => "Test module with API key";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct) => Task.FromResult("ok");
    }

    public sealed class NoApiKeyModule : IVitruvianModule
    {
        public string Domain => "no-api-key";
        public string Description => "Test module without API key";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct) => Task.FromResult("ok");
    }
}
