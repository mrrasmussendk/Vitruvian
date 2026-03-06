using VitruvianAbstractions.Interfaces;
using VitruvianGmailModule;
using VitruvianPluginSdk.Attributes;
using Xunit;

namespace VitruvianTests;

public sealed class GmailMcpModuleTests
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
    public void GmailMcpModule_HasCorrectDomain()
    {
        var module = new GmailMcpModule();

        Assert.Equal("gmail-mcp", module.Domain);
    }

    [Fact]
    public void GmailMcpModule_HasCorrectDescription()
    {
        var module = new GmailMcpModule();

        Assert.Equal("Read, search, and draft Gmail messages using MCP", module.Description);
    }

    [Fact]
    public void GmailMcpModule_RequiresGoogleApiToken()
    {
        var attrs = typeof(GmailMcpModule)
            .GetCustomAttributes(typeof(RequiresApiKeyAttribute), true)
            .Cast<RequiresApiKeyAttribute>()
            .ToList();

        Assert.Single(attrs);
        Assert.Equal("GOOGLE_API_TOKEN", attrs[0].EnvironmentVariable);
    }

    [Fact]
    public void GmailMcpTool_HasRequireApproval()
    {
        var tool = GmailMcpModule.GmailMcpTool;

        Assert.NotNull(tool.Parameters);
        Assert.True(tool.Parameters.ContainsKey("require_approval"));
        Assert.Equal("always", tool.Parameters["require_approval"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoModelClient_ReturnsError()
    {
        var module = new GmailMcpModule();

        var result = await module.ExecuteAsync("read my emails", null, CancellationToken.None);

        Assert.Contains("No model configured", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithModelClient_ReturnsResponse()
    {
        var modelClient = new StubModelClient("You have 3 new emails.");
        var module = new GmailMcpModule(modelClient);

        var result = await module.ExecuteAsync("read my emails", null, CancellationToken.None);

        Assert.Equal("You have 3 new emails.", result);
    }
}
