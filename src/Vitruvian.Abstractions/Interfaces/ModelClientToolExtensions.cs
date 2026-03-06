namespace VitruvianAbstractions.Interfaces;

/// <summary>
/// Delegate invoked when the model requests a tool call during an execution loop.
/// Receives the tool name and arguments JSON, and returns the tool output that is
/// fed back to the model.
/// </summary>
public delegate Task<string> ToolHandler(string toolName, string? toolArguments, CancellationToken cancellationToken);

/// <summary>
/// Extension methods that simplify tool-based interactions with <see cref="IModelClient"/>.
/// Provides a tool execution loop that automatically handles tool calls from the model,
/// invokes the appropriate handler, and feeds results back until a final text response is returned.
/// </summary>
public static class ModelClientToolExtensions
{
    private const int DefaultMaxToolRounds = 10;

    /// <summary>
    /// Sends a request with tools and automatically executes tool calls using the provided handler.
    /// The loop continues until the model returns a text response without requesting any tools,
    /// or until <paramref name="maxRounds"/> is exceeded.
    /// </summary>
    /// <param name="client">The model client.</param>
    /// <param name="systemMessage">System message for the model.</param>
    /// <param name="userMessage">User message / prompt.</param>
    /// <param name="tools">Available tools.</param>
    /// <param name="toolHandler">Callback invoked for each tool call.</param>
    /// <param name="maxRounds">Maximum number of tool call rounds (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final text response from the model after all tool calls are resolved.</returns>
    public static async Task<string> ExecuteWithToolsAsync(
        this IModelClient client,
        string systemMessage,
        string userMessage,
        IReadOnlyList<ModelTool> tools,
        ToolHandler toolHandler,
        int maxRounds = DefaultMaxToolRounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(toolHandler);

        var currentPrompt = userMessage;
        for (var round = 0; round < maxRounds; round++)
        {
            var response = await client.GenerateAsync(new ModelRequest
            {
                Prompt = currentPrompt,
                SystemMessage = systemMessage,
                Tools = tools
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(response.ToolCall))
                return response.Text;

            var toolResult = await toolHandler(response.ToolCall, response.ToolArguments, cancellationToken);
            currentPrompt = $"Tool '{response.ToolCall}' returned:\n{toolResult}\n\nContinue with the original request: {userMessage}";
        }

        throw new InvalidOperationException($"Tool execution loop exceeded the maximum of {maxRounds} rounds.");
    }

    /// <summary>
    /// Sends a request with tools and automatically routes tool calls to a dictionary of named handlers.
    /// Unrecognized tool names return an error message to the model.
    /// </summary>
    /// <param name="client">The model client.</param>
    /// <param name="systemMessage">System message for the model.</param>
    /// <param name="userMessage">User message / prompt.</param>
    /// <param name="tools">Available tools.</param>
    /// <param name="handlers">Map of tool name → async handler function.</param>
    /// <param name="maxRounds">Maximum number of tool call rounds (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final text response from the model after all tool calls are resolved.</returns>
    public static Task<string> ExecuteWithToolsAsync(
        this IModelClient client,
        string systemMessage,
        string userMessage,
        IReadOnlyList<ModelTool> tools,
        IReadOnlyDictionary<string, Func<string?, CancellationToken, Task<string>>> handlers,
        int maxRounds = DefaultMaxToolRounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        return client.ExecuteWithToolsAsync(
            systemMessage,
            userMessage,
            tools,
            async (toolName, toolArgs, ct) =>
            {
                if (handlers.TryGetValue(toolName, out var handler))
                    return await handler(toolArgs, ct);

                return $"Error: Unknown tool '{toolName}'. Available tools: {string.Join(", ", handlers.Keys)}.";
            },
            maxRounds,
            cancellationToken);
    }

    /// <summary>
    /// Sends a rich <see cref="ModelRequest"/> with tools and returns a <see cref="ModelResponse"/>
    /// that includes tool call information when the model requests a tool invocation.
    /// This is a convenience wrapper for modules that need access to the full response
    /// including <see cref="ModelResponse.ToolCall"/> and <see cref="ModelResponse.ToolArguments"/>.
    /// </summary>
    public static async Task<ModelResponse> CompleteWithToolInfoAsync(
        this IModelClient client,
        string systemMessage,
        string userMessage,
        IReadOnlyList<ModelTool>? tools = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.GenerateAsync(new ModelRequest
        {
            Prompt = userMessage,
            SystemMessage = systemMessage,
            Tools = tools
        }, cancellationToken);
    }
}
