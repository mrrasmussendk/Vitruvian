namespace UtilityAi.Compass.PluginHost;

/// <summary>Describes a loaded plugin assembly and its discovered types.</summary>
/// <param name="AssemblyName">The simple name of the plugin assembly.</param>
/// <param name="Version">The assembly version, or <c>null</c> if unavailable.</param>
/// <param name="ModuleTypes">Fully qualified names of discovered <see cref="Abstractions.Interfaces.ICapabilityModule"/> types.</param>
/// <param name="SensorTypes">Fully qualified names of discovered <see cref="UtilityAi.Sensor.ISensor"/> types.</param>
/// <param name="SinkTypes">Fully qualified names of discovered <see cref="UtilityAi.Orchestration.IOrchestrationSink"/> types.</param>
/// <param name="RequiredPermissions">The combined file/resource permissions required by all modules in this plugin.</param>
public sealed record PluginManifest(
    string AssemblyName,
    string? Version,
    IReadOnlyList<string> ModuleTypes,
    IReadOnlyList<string> SensorTypes,
    IReadOnlyList<string> SinkTypes,
    Abstractions.ModuleAccess RequiredPermissions = Abstractions.ModuleAccess.None
);
