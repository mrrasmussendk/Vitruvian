using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.Cli;

/// <summary>
/// Wraps an IModelClient to automatically inject conversation context into requests.
/// This keeps modules simple - they don't need to manage context themselves.
/// </summary>
internal sealed class ContextAwareModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private readonly List<(string User, string Assistant)> _conversationHistory;

    public ContextAwareModelClient(IModelClient inner, List<(string User, string Assistant)> conversationHistory)
    {
        _inner = inner;
        _conversationHistory = conversationHistory;
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // For simple generation, add context as prefix if available
        if (_conversationHistory.Count > 0)
        {
            var context = BuildContextPrefix();
            return _inner.GenerateAsync(context + prompt, cancellationToken);
        }

        return _inner.GenerateAsync(prompt, cancellationToken);
    }

    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        // For rich requests, inject context into system message
        if (_conversationHistory.Count > 0)
        {
            var contextualSystemMessage = (request.SystemMessage ?? "") + "\n\n" + BuildContextForSystemMessage();
            var contextualRequest = request with { SystemMessage = contextualSystemMessage };
            return _inner.GenerateAsync(contextualRequest, cancellationToken);
        }

        return _inner.GenerateAsync(request, cancellationToken);
    }

    public Task<string> CompleteAsync(
        string systemMessage,
        string userMessage,
        IReadOnlyList<ModelTool>? tools = null,
        CancellationToken cancellationToken = default)
    {
        // Inject conversation history into system message
        if (_conversationHistory.Count > 0)
        {
            var contextualSystemMessage = systemMessage + "\n\n" + BuildContextForSystemMessage();
            return _inner.CompleteAsync(contextualSystemMessage, userMessage, tools, cancellationToken);
        }

        return _inner.CompleteAsync(systemMessage, userMessage, tools, cancellationToken);
    }

    private string BuildContextPrefix()
    {
        if (_conversationHistory.Count == 0)
            return string.Empty;

        var recent = _conversationHistory.TakeLast(2);
        var context = "Previous conversation:\n";
        foreach (var (user, assistant) in recent)
        {
            context += $"User: {Truncate(user, 100)}\n";
            context += $"Assistant: {Truncate(assistant, 150)}\n";
        }
        context += "\nCurrent request:\n";
        return context;
    }

    private string BuildContextForSystemMessage()
    {
        if (_conversationHistory.Count == 0)
            return string.Empty;

        var recent = _conversationHistory.TakeLast(2);
        var context = "Recent conversation history:";
        foreach (var (user, assistant) in recent)
        {
            context += $"\nUser: {Truncate(user, 100)}";
            context += $"\nAssistant: {Truncate(assistant, 150)}";
        }
        return context;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "...";
    }
}
