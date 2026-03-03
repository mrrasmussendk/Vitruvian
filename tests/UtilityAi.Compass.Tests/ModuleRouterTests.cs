using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Runtime.Routing;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class ModuleRouterTests
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

    private sealed class TestModule : ICompassModule
    {
        public string Domain { get; }
        public string Description { get; }

        public TestModule(string domain, string description)
        {
            Domain = domain;
            Description = description;
        }

        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult($"{Domain} executed");
    }

    [Fact]
    public async Task SelectModuleAsync_WithNoModules_ReturnsNull()
    {
        var router = new ModuleRouter();

        var result = await router.SelectModuleAsync("test request", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SelectModuleAsync_WithModelClient_UsesLlmSelection()
    {
        var modelClient = new StubModelClient("{\"domain\":\"test-module\",\"confidence\":0.9,\"reason\":\"matches\"}");
        var router = new ModuleRouter(modelClient);
        var module = new TestModule("test-module", "A test module");
        router.RegisterModule(module);

        var result = await router.SelectModuleAsync("test request", CancellationToken.None);

        Assert.Equal("test-module", result);
    }

    [Fact]
    public async Task SelectModuleAsync_WithLowConfidence_FallsBackToKeywordMatch()
    {
        var modelClient = new StubModelClient("{\"domain\":\"test-module\",\"confidence\":0.3,\"reason\":\"unsure\"}");
        var router = new ModuleRouter(modelClient);
        var module = new TestModule("test-module", "A test module");
        router.RegisterModule(module);

        var result = await router.SelectModuleAsync("test request", CancellationToken.None);

        // Low LLM confidence triggers fallback to keyword matching,
        // which still finds "test-module" via keyword overlap
        Assert.Equal("test-module", result);
    }

    [Fact]
    public async Task SelectModuleAsync_WithoutModelClient_MatchesBasedOnDescription()
    {
        var router = new ModuleRouter();
        var fileModule = new TestModule("file-operations", "Read or write files on the local filesystem");
        var conversationModule = new TestModule("conversation", "Answer general questions using conversational AI");
        router.RegisterModule(fileModule);
        router.RegisterModule(conversationModule);

        var result = await router.SelectModuleAsync("read colors.txt", CancellationToken.None);

        Assert.Equal("file-operations", result);
    }

    [Fact]
    public async Task SelectModuleAsync_WithoutModelClient_MatchesFileContent()
    {
        var router = new ModuleRouter();
        var fileModule = new TestModule("file-operations", "Read or write files on the local filesystem");
        var conversationModule = new TestModule("conversation", "Answer general questions using conversational AI");
        router.RegisterModule(fileModule);
        router.RegisterModule(conversationModule);

        var result = await router.SelectModuleAsync("give me the content of marc.txt", CancellationToken.None);

        Assert.Equal("file-operations", result);
    }

    [Fact]
    public async Task SelectModuleAsync_WithoutModelClient_MatchesWebSearch()
    {
        var router = new ModuleRouter();
        var searchModule = new TestModule("web-search", "Search the web for current information and recent events");
        var conversationModule = new TestModule("conversation", "Answer general questions");
        router.RegisterModule(searchModule);
        router.RegisterModule(conversationModule);

        var result = await router.SelectModuleAsync("search for latest news", CancellationToken.None);

        Assert.Equal("web-search", result);
    }

    [Fact]
    public async Task SelectModuleAsync_WithoutModelClient_FallsBackToConversation()
    {
        var router = new ModuleRouter();
        var conversation = new TestModule("conversation", "Answer general questions using conversational AI");
        var fileOps = new TestModule("file-operations", "Read or write files on disk");
        router.RegisterModule(conversation);
        router.RegisterModule(fileOps);

        var result = await router.SelectModuleAsync("explain quantum physics", CancellationToken.None);

        Assert.Equal("conversation", result);
    }

    [Fact]
    public async Task SelectModuleAsync_DynamicModule_WorksWithAnyDescription()
    {
        var router = new ModuleRouter();
        var smsModule = new TestModule("sms", "Send SMS text messages to phone numbers");
        var emailModule = new TestModule("email", "Send and receive email messages");
        var conversationModule = new TestModule("conversation", "Answer general questions");
        router.RegisterModule(smsModule);
        router.RegisterModule(emailModule);
        router.RegisterModule(conversationModule);

        var result = await router.SelectModuleAsync("send a text message to John", CancellationToken.None);

        Assert.Equal("sms", result);
    }
}
