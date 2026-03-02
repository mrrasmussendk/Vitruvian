using UtilityAi.Compass.Abstractions.Facts;

namespace UtilityAi.Compass.Runtime.DI;

/// <summary>
/// Configuration options for Compass DI registration via <see cref="ServiceCollectionExtensions.AddUtilityAiCompass"/>.
/// </summary>
public sealed class CompassOptions
{
    /// <summary>
    /// Gets or sets the working directory for file operations.
    /// When not set, uses the current directory. Relative file paths in file operations are resolved relative to this directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the SQLite connection string used for durable memory.
    /// When not set, Compass uses a local <c>appdb/compass-memory.db</c> file under the app base directory.
    /// </summary>
    public string? MemoryConnectionString { get; set; }

    /// <summary>Gets or sets the <see cref="Facts.GovernanceConfig"/> used by the governance strategy.</summary>
    public GovernanceConfig GovernanceConfig { get; set; } = new();

    /// <summary>Gets or sets whether the <see cref="Modules.GovernanceFinalizerModule"/> is registered.</summary>
    public bool EnableGovernanceFinalizer { get; set; } = true;

    /// <summary>Gets or sets whether Human-in-the-Loop gating is enabled.</summary>
    public bool EnableHitl { get; set; } = false;

    /// <summary>Gets or sets the cooldown keys whose state is tracked each tick.</summary>
    public List<string> TrackedCooldownKeys { get; set; } = [];

    /// <summary>Gets or sets whether the scheduled-command module and its background service are registered.</summary>
    public bool EnableScheduler { get; set; } = false;

    /// <summary>Gets or sets the polling interval for the scheduler background service. Defaults to 15 seconds.</summary>
    public TimeSpan? SchedulerPollInterval { get; set; }
}
