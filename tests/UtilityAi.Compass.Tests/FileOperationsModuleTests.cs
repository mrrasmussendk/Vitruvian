using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.StandardModules;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class FileOperationsModuleTests
{
    private sealed class StubModelClient : IModelClient
    {
        private readonly string _response;

        public StubModelClient(string response)
        {
            _response = response;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModelResponse { Text = _response });

        public Task<string> CompleteAsync(string systemMessage, string userMessage, IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);
    }

    [Fact]
    public void FileOperationsModule_HasCorrectMetadata()
    {
        var module = new FileOperationsModule();

        Assert.Equal("file-operations", module.Domain);
        Assert.Equal("Read or write files on the local filesystem", module.Description);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoFilePath_ReturnsError()
    {
        var module = new FileOperationsModule();

        var result = await module.ExecuteAsync("do something", null, CancellationToken.None);

        Assert.Contains("Could not identify a file path", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentFile_ReturnsNotFound()
    {
        var module = new FileOperationsModule();
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

        var result = await module.ExecuteAsync($"read {nonExistentFile}", null, CancellationToken.None);

        Assert.Contains("File not found", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReadExistingFile_ReturnsContent()
    {
        var module = new FileOperationsModule();
        var tempFile = Path.GetTempFileName();
        var expectedContent = "test content";
        await File.WriteAllTextAsync(tempFile, expectedContent);

        try
        {
            var result = await module.ExecuteAsync($"read {tempFile}", null, CancellationToken.None);

            Assert.Equal(expectedContent, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WriteFile_CreatesFile()
    {
        var modelClient = new StubModelClient("{\"type\":\"write\",\"path\":\"test.txt\",\"content\":\"hello world\"}");
        var module = new FileOperationsModule(modelClient);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var testFile = Path.Combine(tempDir, "test.txt");

        try
        {
            // Change to temp directory for test
            var originalDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDir);

            var result = await module.ExecuteAsync("create test.txt with hello world", null, CancellationToken.None);

            Assert.Contains("File created", result);
            Assert.True(File.Exists(testFile));
            Assert.Equal("hello world", await File.ReadAllTextAsync(testFile));

            Directory.SetCurrentDirectory(originalDir);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithAbsolutePath_RejectsForSecurity()
    {
        var modelClient = new StubModelClient("{\"type\":\"write\",\"path\":\"C:\\\\test.txt\",\"content\":\"hello\"}");
        var module = new FileOperationsModule(modelClient);

        var result = await module.ExecuteAsync("write to C:\\test.txt", null, CancellationToken.None);

        Assert.Contains("Absolute paths not allowed", result);
    }
}
