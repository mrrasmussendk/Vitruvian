using System.Runtime.Loader;
using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.PluginHost;

/// <summary>
/// Runs a module inside an isolated <see cref="AssemblyLoadContext"/> with
/// resource limits enforced by a <see cref="ISandboxPolicy"/>.
/// </summary>
public sealed class SandboxedModuleRunner
{
    private readonly ISandboxPolicy _policy;

    /// <summary>
    /// Initializes a new instance of <see cref="SandboxedModuleRunner"/>.
    /// </summary>
    /// <param name="policy">The sandbox policy defining resource limits and API restrictions.</param>
    public SandboxedModuleRunner(ISandboxPolicy policy)
    {
        _policy = policy;
    }

    /// <summary>
    /// Executes a module within the sandbox, enforcing wall-time limits.
    /// </summary>
    /// <param name="module">The module to execute.</param>
    /// <param name="request">The user's request text.</param>
    /// <param name="userId">Optional user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The module's response text.</returns>
    /// <exception cref="TimeoutException">Thrown when the module exceeds the wall-time limit.</exception>
    public async Task<string> ExecuteAsync(ICompassModule module, string request, string? userId, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(_policy.MaxWallTime);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await module.ExecuteAsync(request, userId, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Module '{module.Domain}' exceeded the wall-time limit of {_policy.MaxWallTime.TotalSeconds}s.");
        }
    }

    /// <summary>
    /// Loads a plugin assembly into an isolated <see cref="AssemblyLoadContext"/>.
    /// </summary>
    /// <param name="assemblyPath">The path to the plugin assembly.</param>
    /// <returns>The collectible load context for the plugin.</returns>
    public static AssemblyLoadContext CreateIsolatedContext(string assemblyPath)
    {
        var contextName = $"Sandbox-{Path.GetFileNameWithoutExtension(assemblyPath)}";
        return new AssemblyLoadContext(contextName, isCollectible: true);
    }
}
