using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.StandardModules;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class ShellCommandModuleTests
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
    public void ShellCommandModule_HasCorrectMetadata()
    {
        var module = new ShellCommandModule(workingDirectory: Path.GetTempPath());

        Assert.Equal("shell-command", module.Domain);
        Assert.Contains("command-line", module.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_DisallowedCommand_ReturnsGuardrailError()
    {
        var modelClient = new StubModelClient("""{"command":"bash","args":["-lc","echo hi"]}""");
        var module = new ShellCommandModule(modelClient, workingDirectory: Path.GetTempPath());

        var result = await module.ExecuteAsync("run bash", null, CancellationToken.None);

        Assert.Contains("is not allowed", result);
        Assert.Contains("Allowed commands", result);
    }

    [Fact]
    public async Task ExecuteAsync_AllowedCommand_ExecutesAndReturnsOutput()
    {
        var modelClient = new StubModelClient("""{"command":"dotnet","args":["--version"]}""");
        var module = new ShellCommandModule(modelClient, workingDirectory: Path.GetTempPath());

        var result = await module.ExecuteAsync("run dotnet --version", null, CancellationToken.None);

        Assert.Contains("ExitCode: 0", result);
        Assert.Contains(".", result);
    }

    [Fact]
    public async Task ExecuteAsync_NoModelClient_ParsesRawRequest()
    {
        var module = new ShellCommandModule(workingDirectory: Path.GetTempPath());

        var result = await module.ExecuteAsync("dotnet --version", null, CancellationToken.None);

        Assert.Contains("ExitCode: 0", result);
    }
}
