namespace VitruvianPluginSdk.Attributes;

/// <summary>
/// Declares an environment variable containing an API key that must be configured for this module.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresApiKeyAttribute : Attribute
{
    /// <summary>Gets the environment variable name for the required API key.</summary>
    public string EnvironmentVariable { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="RequiresApiKeyAttribute"/>.
    /// </summary>
    /// <param name="environmentVariable">Environment variable name that stores the API key.</param>
    public RequiresApiKeyAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(environmentVariable))
            throw new ArgumentException("Environment variable name cannot be empty.", nameof(environmentVariable));

        EnvironmentVariable = environmentVariable;
    }
}
