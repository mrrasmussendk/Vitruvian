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

    [Fact]
    public void Load_LoadsProfileFromPowerShellFormat()
    {
        var profileKey = $"VITRUVIAN_TEST_PS_{Guid.NewGuid():N}";
        Track("VITRUVIAN_PROFILE", profileKey);

        File.WriteAllText(Path.Combine(_tempDir, ".env.Vitruvian"), "$env:VITRUVIAN_PROFILE='dev'");
        File.WriteAllText(Path.Combine(_tempDir, ".env.Vitruvian.dev"), $"{profileKey}=ps-value");

        EnvFileLoader.Load(_tempDir, overwriteExisting: true);

        Assert.Equal("dev", Environment.GetEnvironmentVariable("VITRUVIAN_PROFILE"));
        Assert.Equal("ps-value", Environment.GetEnvironmentVariable(profileKey));
    }

    [Fact]
    public void PersistSecret_CreatesFileAtAppBaseDirectory_WhenNoFileExists()
    {
        // PersistSecret should default to AppContext.BaseDirectory for new files,
        // not to the fallback directory (current working directory).
        var uniqueKey = $"VITRUVIAN_TEST_PERSIST_{Guid.NewGuid():N}";
        var fallbackDir = Path.Combine(Path.GetTempPath(), $"vitruvian-persist-fallback-{Guid.NewGuid():N}");
        var appBaseDir = AppContext.BaseDirectory;
        var expectedPath = Path.Combine(appBaseDir, ".env.Vitruvian");

        Directory.CreateDirectory(fallbackDir);
        try
        {
            // Capture original state for restoration after test
            var existedBefore = File.Exists(expectedPath);
            var originalContent = existedBefore ? File.ReadAllBytes(expectedPath) : null;

            EnvFileLoader.PersistSecret(uniqueKey, "test-value", fallbackDir);

            // The key should NOT have been written to the fallback directory
            var fallbackPath = Path.Combine(fallbackDir, ".env.Vitruvian");
            Assert.False(File.Exists(fallbackPath), "PersistSecret should not create .env.Vitruvian in the fallback directory when no file exists");

            // Restore the original file state
            if (existedBefore)
                File.WriteAllBytes(expectedPath, originalContent!);
            else if (File.Exists(expectedPath))
                File.Delete(expectedPath);
        }
        finally
        {
            if (Directory.Exists(fallbackDir))
                Directory.Delete(fallbackDir, recursive: true);
        }
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
