namespace UtilityAi.Compass.Abstractions.Interfaces;

/// <summary>
/// Describes a tool/skill that can be made available to the model during generation.
/// </summary>
public sealed record ModelTool(string Name, string Description, IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// A rich request that a module sends to the framework-provided model.
/// Modules describe *what* they need; the host decides *how* to fulfil it.
/// </summary>
public sealed record ModelRequest
{
    public required string Prompt { get; init; }
    public string? SystemMessage { get; init; }
    public string? ModelHint { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public IReadOnlyList<ModelTool>? Tools { get; init; }

    public ModelRequest() { }
}

/// <summary>
/// The response returned by the framework after handling a model request.
/// </summary>
public sealed record ModelResponse
{
    public required string Text { get; init; }
    public string? ToolCall { get; init; }
    public string? ToolArguments { get; init; }

    public ModelResponse() { }
}

/// <summary>
/// Framework-level contract that the host injects into plugin modules.
/// Modules depend only on this interface – never on a specific AI provider.
/// The host registers an implementation that routes to whatever provider
/// the user has installed (OpenAI, Anthropic, Gemini, local models, etc.).
/// </summary>
public interface IModelClient
{
    /// <summary>Simple prompt-in / text-out convenience method.</summary>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Rich request supporting model hints, tools, temperature, etc.</summary>
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Simplified completion method with system/user messages and optional tools.
    /// Returns just the text response.
    /// </summary>
    Task<string> CompleteAsync(
        string systemMessage,
        string userMessage,
        IReadOnlyList<ModelTool>? tools = null,
        CancellationToken cancellationToken = default);
}
