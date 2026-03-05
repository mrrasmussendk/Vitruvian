using VitruvianAbstractions.Interfaces;
using Xunit;

namespace VitruvianTests;

public sealed class ModelClientToolExtensionsTests
{
    [Fact]
    public async Task ExecuteWithToolsAsync_WhenNoToolCalled_ReturnsTextDirectly()
    {
        var client = new FakeModelClient(new ModelResponse { Text = "Hello world" });

        var result = await client.ExecuteWithToolsAsync(
            "system",
            "user message",
            [new ModelTool("tool1", "desc")],
            (_, _, _) => Task.FromResult("tool result"),
            cancellationToken: CancellationToken.None);

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public async Task ExecuteWithToolsAsync_WhenToolCalled_InvokesHandler()
    {
        var responses = new Queue<ModelResponse>();
        responses.Enqueue(new ModelResponse { Text = "", ToolCall = "search", ToolArguments = """{"q":"test"}""" });
        responses.Enqueue(new ModelResponse { Text = "Final answer" });

        var client = new FakeModelClient(responses);
        var handlerCalled = false;
        string? capturedToolName = null;
        string? capturedArgs = null;

        var result = await client.ExecuteWithToolsAsync(
            "system",
            "user message",
            [new ModelTool("search", "Search")],
            (toolName, toolArgs, _) =>
            {
                handlerCalled = true;
                capturedToolName = toolName;
                capturedArgs = toolArgs;
                return Task.FromResult("search results");
            },
            cancellationToken: CancellationToken.None);

        Assert.True(handlerCalled);
        Assert.Equal("search", capturedToolName);
        Assert.Equal("""{"q":"test"}""", capturedArgs);
        Assert.Equal("Final answer", result);
    }

    [Fact]
    public async Task ExecuteWithToolsAsync_ExceedsMaxRounds_Throws()
    {
        // Always returns a tool call - never resolves
        var client = new FakeModelClient(new ModelResponse { Text = "", ToolCall = "loop", ToolArguments = null });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ExecuteWithToolsAsync(
                "system",
                "user",
                [new ModelTool("loop", "desc")],
                (_, _, _) => Task.FromResult("looping"),
                maxRounds: 3,
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteWithToolsAsync_DictionaryHandlers_RoutesToCorrectHandler()
    {
        var responses = new Queue<ModelResponse>();
        responses.Enqueue(new ModelResponse { Text = "", ToolCall = "greet", ToolArguments = null });
        responses.Enqueue(new ModelResponse { Text = "Done" });

        var client = new FakeModelClient(responses);
        var greetCalled = false;

        var handlers = new Dictionary<string, Func<string?, CancellationToken, Task<string>>>
        {
            ["greet"] = (_, _) => { greetCalled = true; return Task.FromResult("Hello!"); },
            ["bye"] = (_, _) => Task.FromResult("Goodbye!")
        };

        var result = await client.ExecuteWithToolsAsync(
            "system", "user",
            [new ModelTool("greet", "Greet"), new ModelTool("bye", "Bye")],
            handlers,
            cancellationToken: CancellationToken.None);

        Assert.True(greetCalled);
        Assert.Equal("Done", result);
    }

    [Fact]
    public async Task ExecuteWithToolsAsync_DictionaryHandlers_UnknownToolReturnsError()
    {
        var responses = new Queue<ModelResponse>();
        responses.Enqueue(new ModelResponse { Text = "", ToolCall = "unknown_tool", ToolArguments = null });
        responses.Enqueue(new ModelResponse { Text = "Recovered" });

        var client = new FakeModelClient(responses);

        var handlers = new Dictionary<string, Func<string?, CancellationToken, Task<string>>>
        {
            ["known"] = (_, _) => Task.FromResult("result")
        };

        var result = await client.ExecuteWithToolsAsync(
            "system", "user",
            [new ModelTool("known", "Known")],
            handlers,
            cancellationToken: CancellationToken.None);

        Assert.Equal("Recovered", result);
        // The second prompt should contain the error about unknown tool
        Assert.Contains("unknown_tool", client.LastPrompt);
    }

    [Fact]
    public async Task CompleteWithToolInfoAsync_ReturnsFullModelResponse()
    {
        var expectedResponse = new ModelResponse
        {
            Text = "response text",
            ToolCall = "my_tool",
            ToolArguments = """{"key":"value"}"""
        };
        var client = new FakeModelClient(expectedResponse);

        var result = await client.CompleteWithToolInfoAsync(
            "system", "user",
            [new ModelTool("my_tool", "desc")]);

        Assert.Equal("response text", result.Text);
        Assert.Equal("my_tool", result.ToolCall);
        Assert.Equal("""{"key":"value"}""", result.ToolArguments);
    }

    [Fact]
    public async Task ExecuteWithToolsAsync_ThrowsOnNullClient()
    {
        IModelClient client = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.ExecuteWithToolsAsync(
                "sys", "user",
                [],
                (_, _, _) => Task.FromResult(""),
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteWithToolsAsync_ThrowsOnNullHandler()
    {
        var client = new FakeModelClient(new ModelResponse { Text = "ok" });

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.ExecuteWithToolsAsync(
                "sys", "user",
                [],
                (ToolHandler)null!,
                cancellationToken: CancellationToken.None));
    }

    /// <summary>
    /// Minimal fake model client for testing tool extensions.
    /// </summary>
    private sealed class FakeModelClient : IModelClient
    {
        private readonly Queue<ModelResponse> _responses;
        public string? LastPrompt { get; private set; }

        public FakeModelClient(ModelResponse singleResponse)
        {
            _responses = new Queue<ModelResponse>();
            _responses.Enqueue(singleResponse);
        }

        public FakeModelClient(Queue<ModelResponse> responses)
        {
            _responses = responses;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue().Text : "empty");
        }

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request.Prompt;
            var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(response);
        }

        public Task<string> CompleteAsync(string systemMessage, string userMessage,
            IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
        {
            LastPrompt = userMessage;
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue().Text : "empty");
        }
    }
}
