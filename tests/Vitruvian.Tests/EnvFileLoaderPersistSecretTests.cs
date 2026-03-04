using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class EnvFileLoaderPersistSecretTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"vitruvian-env-{Guid.NewGuid():N}");

    public EnvFileLoaderPersistSecretTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void PersistSecret_CreatesFileWhenMissing()
    {
        EnvFileLoader.PersistSecret("MY_KEY", "my_value", _tempDir);

        var envFile = Path.Combine(_tempDir, ".env.Vitruvian");
        Assert.True(File.Exists(envFile));
        var lines = File.ReadAllLines(envFile);
        Assert.Contains("MY_KEY=my_value", lines);
    }

    [Fact]
    public void PersistSecret_AppendsToExistingFile()
    {
        var envFile = Path.Combine(_tempDir, ".env.Vitruvian");
        File.WriteAllText(envFile, "EXISTING_KEY=existing_value\n");

        EnvFileLoader.PersistSecret("NEW_KEY", "new_value", _tempDir);

        var lines = File.ReadAllLines(envFile);
        Assert.Contains("EXISTING_KEY=existing_value", lines);
        Assert.Contains("NEW_KEY=new_value", lines);
    }

    [Fact]
    public void PersistSecret_UpdatesExistingKey()
    {
        var envFile = Path.Combine(_tempDir, ".env.Vitruvian");
        File.WriteAllText(envFile, "MY_KEY=old_value\nOTHER=keep\n");

        EnvFileLoader.PersistSecret("MY_KEY", "new_value", _tempDir);

        var lines = File.ReadAllLines(envFile);
        Assert.Contains("MY_KEY=new_value", lines);
        Assert.Contains("OTHER=keep", lines);
        Assert.DoesNotContain("MY_KEY=old_value", lines);
    }

    [Fact]
    public void PersistSecret_PreservesComments()
    {
        var envFile = Path.Combine(_tempDir, ".env.Vitruvian");
        File.WriteAllText(envFile, "# This is a comment\nFOO=bar\n");

        EnvFileLoader.PersistSecret("BAZ", "qux", _tempDir);

        var content = File.ReadAllText(envFile);
        Assert.Contains("# This is a comment", content);
        Assert.Contains("FOO=bar", content);
        Assert.Contains("BAZ=qux", content);
    }
}
