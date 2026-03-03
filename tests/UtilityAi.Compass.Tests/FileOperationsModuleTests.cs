using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.StandardModules;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class FileOperationsModuleTests
{
    private sealed class StubModelClient : IModelClient
    {
        private readonly string _response;
        public string? LastSystemMessage { get; private set; }

        public StubModelClient(string response)
        {
            _response = response;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModelResponse { Text = _response });

        public Task<string> CompleteAsync(string systemMessage, string userMessage, IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
        {
            LastSystemMessage = systemMessage;
            return Task.FromResult(_response);
        }
    }

    [Fact]
    public void FileOperationsModule_HasCorrectMetadata()
    {
        var module = new FileOperationsModule(workingDirectory: Path.GetTempPath());

        Assert.Equal("file-operations", module.Domain);
        Assert.Equal("Read content from files or write/create text files with specific filenames (e.g., notes.txt, config.json)", module.Description);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoFilePath_ReturnsError()
    {
        var module = new FileOperationsModule(workingDirectory: Path.GetTempPath());

        var result = await module.ExecuteAsync("do something", null, CancellationToken.None);

        // ExtractFilePath falls back to the last token "something",
        // which is then treated as a file read attempt
        Assert.Contains("File not found", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentFile_ReturnsNotFound()
    {
        var module = new FileOperationsModule(workingDirectory: Path.GetTempPath());
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

        var result = await module.ExecuteAsync($"read {nonExistentFile}", null, CancellationToken.None);

        Assert.Contains("File not found", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReadExistingFile_ReturnsContent()
    {
        var module = new FileOperationsModule(workingDirectory: Path.GetTempPath());
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
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var module = new FileOperationsModule(modelClient, workingDirectory: tempDir);
        var testFile = Path.Combine(tempDir, "test.txt");

        try
        {
            var result = await module.ExecuteAsync("create test.txt with hello world", null, CancellationToken.None);

            Assert.Contains("File created", result);
            Assert.True(File.Exists(testFile));
            Assert.Equal("hello world", await File.ReadAllTextAsync(testFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithAbsolutePath_RejectsForSecurity()
    {
        var absolutePath = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "test.txt");
        var jsonPath = absolutePath.Replace("\\", "\\\\");
        var modelClient = new StubModelClient($"{{\"type\":\"write\",\"path\":\"{jsonPath}\",\"content\":\"hello\"}}");
        var module = new FileOperationsModule(modelClient, workingDirectory: Path.GetTempPath());

        var result = await module.ExecuteAsync($"write to {absolutePath}", null, CancellationToken.None);

        Assert.Contains("Absolute paths not allowed", result);
    }

    [Fact]
    public async Task ExecuteAsync_UsesEmbeddedSkillPrompt_ForOperationDetection()
    {
        var modelClient = new StubModelClient("{\"type\":\"write\",\"path\":\"test.txt\",\"content\":\"hello\"}");
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var module = new FileOperationsModule(modelClient, workingDirectory: tempDir);

        try
        {
            await module.ExecuteAsync("create test.txt with hello", null, CancellationToken.None);

            Assert.Contains("FILE_OPERATIONS_SKILL_V1", modelClient.LastSystemMessage);
            Assert.Contains("Determine the file operation type and extract parameters.", modelClient.LastSystemMessage);
            Assert.Contains(@"{""type"":""read""|""write""", modelClient.LastSystemMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
