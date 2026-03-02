using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.PluginSdk.Attributes;

namespace UtilityAi.Compass.Runtime;

/// <summary>
/// Validates that a module has the required permissions before execution.
/// Reads <see cref="RequiresPermissionAttribute"/> from the module type and checks
/// against the provided <see cref="IPermissionContext"/>.
/// </summary>
public sealed class PermissionChecker
{
    private readonly IPermissionContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="PermissionChecker"/>.
    /// </summary>
    /// <param name="context">The permission context to validate against.</param>
    public PermissionChecker(IPermissionContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Checks whether the given module has all required permissions.
    /// </summary>
    /// <param name="module">The module to check.</param>
    /// <param name="userId">The user requesting execution.</param>
    /// <param name="group">The group the user belongs to.</param>
    /// <returns><c>true</c> if all required permissions are granted; otherwise <c>false</c>.</returns>
    public bool IsAllowed(ICompassModule module, string userId, string group)
    {
        var required = GetRequiredAccess(module.GetType());
        if (required == ModuleAccess.None)
            return true;

        return _context.HasPermission(userId, group, required);
    }

    /// <summary>
    /// Checks permissions and throws <see cref="PermissionDeniedException"/> if denied.
    /// </summary>
    /// <param name="module">The module to check.</param>
    /// <param name="userId">The user requesting execution.</param>
    /// <param name="group">The group the user belongs to.</param>
    public void Enforce(ICompassModule module, string userId, string group)
    {
        var required = GetRequiredAccess(module.GetType());
        if (required == ModuleAccess.None)
            return;

        if (!_context.HasPermission(userId, group, required))
            throw new PermissionDeniedException(module.Domain, required);
    }

    /// <summary>
    /// Reads the combined <see cref="ModuleAccess"/> requirements from a module type's
    /// <see cref="RequiresPermissionAttribute"/> annotations.
    /// </summary>
    public static ModuleAccess GetRequiredAccess(Type moduleType)
    {
        var attrs = moduleType.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true);
        var combined = ModuleAccess.None;
        foreach (RequiresPermissionAttribute attr in attrs)
            combined |= attr.Access;
        return combined;
    }
}
