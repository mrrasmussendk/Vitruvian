using VitruvianAbstractions.Interfaces;
using VitruvianGoogleCalendarModule;
using VitruvianPluginSdk.Attributes;
using Xunit;

namespace VitruvianTests;

public sealed class GoogleCalendarMcpModuleTests
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
    public void GoogleCalendarMcpModule_HasCorrectDomain()
    {
        var module = new GoogleCalendarMcpModule();

        Assert.Equal("google-calendar-mcp", module.Domain);
    }

    [Fact]
    public void GoogleCalendarMcpModule_HasCorrectDescription()
    {
        var module = new GoogleCalendarMcpModule();

        Assert.Equal("Read, create, and manage Google Calendar events using MCP", module.Description);
    }

    [Fact]
    public void GoogleCalendarMcpModule_RequiresGoogleApiToken()
    {
        var attrs = typeof(GoogleCalendarMcpModule)
            .GetCustomAttributes(typeof(RequiresApiKeyAttribute), true)
            .Cast<RequiresApiKeyAttribute>()
            .ToList();

        Assert.Single(attrs);
        Assert.Equal("GOOGLE_API_TOKEN", attrs[0].EnvironmentVariable);
    }

    [Fact]
    public void GoogleCalendarMcpTool_HasRequireApproval()
    {
        var tool = GoogleCalendarMcpModule.GoogleCalendarMcpTool;

        Assert.NotNull(tool.Parameters);
        Assert.True(tool.Parameters.ContainsKey("require_approval"));
        Assert.Equal("never", tool.Parameters["require_approval"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoModelClient_ReturnsError()
    {
        var module = new GoogleCalendarMcpModule();

        var result = await module.ExecuteAsync("show my calendar events", null, CancellationToken.None);

        Assert.Contains("No model configured", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithModelClient_ReturnsResponse()
    {
        var modelClient = new StubModelClient("You have 2 events today.");
        var module = new GoogleCalendarMcpModule(modelClient);

        var result = await module.ExecuteAsync("show my calendar events", null, CancellationToken.None);

        Assert.Equal("You have 2 events today.", result);
    }
}
