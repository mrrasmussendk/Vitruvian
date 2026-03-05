using VitruvianAbstractions.Interfaces;
using VitruvianStandardModules;
using Xunit;

namespace VitruvianTests;

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

    private sealed class StubCommandRunner : ICommandRunner
    {
        public string? Command { get; private set; }
        public IReadOnlyList<string>? Args { get; private set; }
        public string? WorkingDirectory { get; private set; }
        public TimeSpan? Timeout { get; private set; }

        public Task<CommandExecutionResult> ExecuteAsync(
            string command,
            IReadOnlyList<string> args,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            Args = args;
            WorkingDirectory = workingDirectory;
            Timeout = timeout;
            return Task.FromResult(new CommandExecutionResult(0, "ok", string.Empty));
        }
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

    [Fact]
    public async Task ExecuteAsync_UsesInjectedCommandRunner()
    {
        var modelClient = new StubModelClient("""{"command":"dotnet","args":["--version"]}""");
        var runner = new StubCommandRunner();
        var workingDirectory = Path.GetTempPath();
        var module = new ShellCommandModule(modelClient, workingDirectory, commandRunner: runner);

        var result = await module.ExecuteAsync("run dotnet --version", null, CancellationToken.None);

        Assert.Equal("dotnet", runner.Command);
        Assert.Equal(["--version"], runner.Args);
        Assert.Equal(workingDirectory, runner.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(15), runner.Timeout);
        Assert.Contains("ExitCode: 0", result);
        Assert.Contains("ok", result);
    }
}
