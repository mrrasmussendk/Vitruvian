namespace VitruvianAbstractions.Interfaces;

/// <summary>
/// Fluent builder for creating <see cref="ModelTool"/> instances.
/// Reduces boilerplate when defining tools with typed parameters.
/// <example>
/// <code>
/// var tool = new ModelToolBuilder("search_web", "Search the web")
///     .AddParameter("query", "Search query text")
///     .AddParameter("maxResults", "Maximum number of results")
///     .Build();
/// </code>
/// </example>
/// </summary>
public sealed class ModelToolBuilder
{
    private readonly string _name;
    private readonly string _description;
    private readonly Dictionary<string, string> _parameters = new(StringComparer.Ordinal);

    public ModelToolBuilder(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        _name = name;
        _description = description;
    }

    /// <summary>
    /// Adds a parameter to the tool definition.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="description">Human-readable description of the parameter (also used as the type hint).</param>
    /// <returns>This builder for chaining.</returns>
    public ModelToolBuilder AddParameter(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _parameters[name] = description ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="ModelTool"/> instance.
    /// </summary>
    public ModelTool Build()
        => new(_name, _description, _parameters.Count > 0 ? new Dictionary<string, string>(_parameters) : null);
}
