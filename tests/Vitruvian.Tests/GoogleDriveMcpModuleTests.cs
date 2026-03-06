using VitruvianAbstractions.Interfaces;
using VitruvianGoogleDriveModule;
using VitruvianPluginSdk.Attributes;
using Xunit;

namespace VitruvianTests;

public sealed class GoogleDriveMcpModuleTests
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
    public void GoogleDriveMcpModule_HasCorrectDomain()
    {
        var module = new GoogleDriveMcpModule();

        Assert.Equal("google-drive-mcp", module.Domain);
    }

    [Fact]
    public void GoogleDriveMcpModule_HasCorrectDescription()
    {
        var module = new GoogleDriveMcpModule();

        Assert.Equal("Read, search, and manage Google Drive files using MCP", module.Description);
    }

    [Fact]
    public void GoogleDriveMcpModule_RequiresGoogleDriveToken()
    {
        var attrs = typeof(GoogleDriveMcpModule)
            .GetCustomAttributes(typeof(RequiresApiKeyAttribute), true)
            .Cast<RequiresApiKeyAttribute>()
            .ToList();

        Assert.Single(attrs);
        Assert.Equal("GOOGLE_DRIVE_TOKEN", attrs[0].EnvironmentVariable);
    }

    [Fact]
    public void GoogleDriveMcpTool_HasRequireApproval()
    {
        var tool = GoogleDriveMcpModule.GoogleDriveMcpTool;

        Assert.NotNull(tool.Parameters);
        Assert.True(tool.Parameters.ContainsKey("require_approval"));
        Assert.Equal("never", tool.Parameters["require_approval"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoModelClient_ReturnsError()
    {
        var module = new GoogleDriveMcpModule();

        var result = await module.ExecuteAsync("list my files", null, CancellationToken.None);

        Assert.Contains("No model configured", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithModelClient_ReturnsResponse()
    {
        var modelClient = new StubModelClient("You have 10 files in your Drive.");
        var module = new GoogleDriveMcpModule(modelClient);

        var result = await module.ExecuteAsync("list my files", null, CancellationToken.None);

        Assert.Equal("You have 10 files in your Drive.", result);
    }
}
