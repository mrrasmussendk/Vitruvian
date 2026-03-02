namespace UtilityAi.Compass.Abstractions.Interfaces;

/// <summary>
/// Defines the resource limits and API restrictions for a sandboxed module execution context.
/// </summary>
public interface ISandboxPolicy
{
    /// <summary>Gets the maximum CPU time allowed for a single module execution.</summary>
    TimeSpan MaxCpuTime { get; }

    /// <summary>Gets the maximum memory in bytes the module is allowed to allocate.</summary>
    long MaxMemoryBytes { get; }

    /// <summary>Gets the maximum wall-clock time allowed for a single module execution.</summary>
    TimeSpan MaxWallTime { get; }

    /// <summary>Gets whether the module is allowed to access the file system.</summary>
    bool AllowFileSystem { get; }

    /// <summary>Gets whether the module is allowed to make network requests.</summary>
    bool AllowNetwork { get; }

    /// <summary>Gets whether the module is allowed to spawn processes.</summary>
    bool AllowProcessSpawn { get; }
}

/// <summary>
/// Default sandbox policy with sensible resource limits for untrusted modules.
/// </summary>
public sealed class DefaultSandboxPolicy : ISandboxPolicy
{
    /// <inheritdoc />
    public TimeSpan MaxCpuTime { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public long MaxMemoryBytes { get; init; } = 256 * 1024 * 1024; // 256 MB

    /// <inheritdoc />
    public TimeSpan MaxWallTime { get; init; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public bool AllowFileSystem { get; init; }

    /// <inheritdoc />
    public bool AllowNetwork { get; init; }

    /// <inheritdoc />
    public bool AllowProcessSpawn { get; init; }
}
