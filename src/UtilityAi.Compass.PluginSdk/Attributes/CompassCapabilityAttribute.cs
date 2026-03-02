namespace UtilityAi.Compass.PluginSdk.Attributes;

/// <summary>Marks a capability module with its domain name and natural language description.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CompassCapabilityAttribute : Attribute
{
    /// <summary>Gets the domain identifier for this capability (e.g. "search", "code-gen").</summary>
    public string Domain { get; }

    /// <summary>Gets the natural language description of what this module does.</summary>
    public string Description { get; }

    /// <summary>Initializes a new instance of <see cref="CompassCapabilityAttribute"/>.</summary>
    /// <param name="domain">The domain identifier for this capability.</param>
    /// <param name="description">Natural language description of the capability.</param>
    public CompassCapabilityAttribute(string domain, string description)
    {
        Domain = domain;
        Description = description;
    }
}
