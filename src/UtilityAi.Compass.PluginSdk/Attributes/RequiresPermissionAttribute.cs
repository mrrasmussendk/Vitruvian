using UtilityAi.Compass.Abstractions;

namespace UtilityAi.Compass.PluginSdk.Attributes;

/// <summary>
/// Declares the file/resource permissions that a capability module requires.
/// The runtime enforces these permissions before allowing module execution.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : Attribute
{
    /// <summary>Gets the <see cref="ModuleAccess"/> level this module requires.</summary>
    public ModuleAccess Access { get; }

    /// <summary>Gets the optional resource path or pattern the permission applies to (e.g. "files/*").</summary>
    public string? Resource { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="RequiresPermissionAttribute"/>.
    /// </summary>
    /// <param name="access">The access level required.</param>
    /// <param name="resource">Optional resource path or pattern.</param>
    public RequiresPermissionAttribute(ModuleAccess access, string? resource = null)
    {
        Access = access;
        Resource = resource;
    }
}
