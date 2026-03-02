namespace UtilityAi.Compass.Runtime;

/// <summary>
/// Exception thrown when a module does not have the required permissions to execute.
/// </summary>
public sealed class PermissionDeniedException : Exception
{
    /// <summary>Gets the domain of the module that was denied.</summary>
    public string ModuleDomain { get; }

    /// <summary>Gets the required access that was missing.</summary>
    public Abstractions.ModuleAccess RequiredAccess { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PermissionDeniedException"/>.
    /// </summary>
    public PermissionDeniedException(string moduleDomain, Abstractions.ModuleAccess requiredAccess)
        : base($"Permission denied: module '{moduleDomain}' requires {requiredAccess} access.")
    {
        ModuleDomain = moduleDomain;
        RequiredAccess = requiredAccess;
    }
}
