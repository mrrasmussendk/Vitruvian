using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class EnvFileLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"vitruvian-env-profile-{Guid.NewGuid():N}");
    private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

    public EnvFileLoaderTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void Load_LoadsActiveProfileFromDottedFile()
    {
        var profileKey = $"VITRUVIAN_TEST_PROFILE_{Guid.NewGuid():N}";
        var sharedKey = $"VITRUVIAN_TEST_SHARED_{Guid.NewGuid():N}";
        Track("VITRUVIAN_PROFILE", profileKey, sharedKey);

        File.WriteAllText(Path.Combine(_tempDir, ".env.Vitruvian"), $"VITRUVIAN_PROFILE=dev{Environment.NewLine}{sharedKey}=base");
        File.WriteAllText(Path.Combine(_tempDir, ".env.Vitruvian.dev"), $"{profileKey}=from-profile{Environment.NewLine}{sharedKey}=profile");

        EnvFileLoader.Load(_tempDir, overwriteExisting: true);

        Assert.Equal("from-profile", Environment.GetEnvironmentVariable(profileKey));
        Assert.Equal("profile", Environment.GetEnvironmentVariable(sharedKey));
    }

    [Fact]
    public void Load_LoadsActiveProfileFromLegacyUndottedFile()
    {
        var legacyKey = $"VITRUVIAN_TEST_LEGACY_{Guid.NewGuid():N}";
        Track("VITRUVIAN_PROFILE", legacyKey);

        File.WriteAllText(Path.Combine(_tempDir, ".env.Vitruvian"), "VITRUVIAN_PROFILE=team");
        File.WriteAllText(Path.Combine(_tempDir, ".env.Vitruvianteam"), $"{legacyKey}=legacy-value");

        EnvFileLoader.Load(_tempDir, overwriteExisting: true);

        Assert.Equal("legacy-value", Environment.GetEnvironmentVariable(legacyKey));
    }

    public void Dispose()
    {
        foreach (var (key, originalValue) in _originalValues)
            Environment.SetEnvironmentVariable(key, originalValue);

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void Track(params string[] variableNames)
    {
        foreach (var variableName in variableNames)
            _originalValues.TryAdd(variableName, Environment.GetEnvironmentVariable(variableName));
    }
}
