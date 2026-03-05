using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianRuntime;
using VitruvianStandardModules;
using Xunit;

namespace VitruvianTests;

public sealed class ConversationModuleTests
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
    public void ConversationModule_HasCorrectMetadata()
    {
        var module = new ConversationModule();

        Assert.Equal("conversation", module.Domain);
        Assert.Equal("Answer general questions, provide explanations, and have conversations (fallback for queries not handled by specialized modules)", module.Description);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoModelClient_ReturnsError()
    {
        var module = new ConversationModule();

        var result = await module.ExecuteAsync("hello", null, CancellationToken.None);

        Assert.Contains("No model configured", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithModelClient_ReturnsResponse()
    {
        var modelClient = new StubModelClient("Hello! How can I help?");
        var module = new ConversationModule(modelClient);

        var result = await module.ExecuteAsync("hello", null, CancellationToken.None);

        Assert.Equal("Hello! How can I help?", result);
    }

    [Fact]
    public void ConversationModule_DeclaresRequiresPermission()
    {
        var access = PermissionChecker.GetRequiredAccess(typeof(ConversationModule));

        Assert.True(access.HasFlag(ModuleAccess.Read));
    }
}
