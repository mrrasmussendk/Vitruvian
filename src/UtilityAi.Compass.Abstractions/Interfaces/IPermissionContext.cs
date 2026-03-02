namespace UtilityAi.Compass.Abstractions.Interfaces;

/// <summary>
/// Provides the permission context for the current execution,
/// including user, group, and other permission levels (Linux-style model).
/// </summary>
public interface IPermissionContext
{
    /// <summary>Gets the identifier of the current user.</summary>
    string UserId { get; }

    /// <summary>Gets the group the current user belongs to.</summary>
    string Group { get; }

    /// <summary>Gets the <see cref="ModuleAccess"/> permissions granted to the owning user.</summary>
    ModuleAccess UserPermissions { get; }

    /// <summary>Gets the <see cref="ModuleAccess"/> permissions granted to the owning group.</summary>
    ModuleAccess GroupPermissions { get; }

    /// <summary>Gets the <see cref="ModuleAccess"/> permissions granted to all other users.</summary>
    ModuleAccess OtherPermissions { get; }

    /// <summary>
    /// Determines whether the specified <paramref name="access"/> is permitted
    /// for the given <paramref name="userId"/> and <paramref name="group"/>.
    /// </summary>
    bool HasPermission(string userId, string group, ModuleAccess access);
}
