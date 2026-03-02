using Microsoft.Extensions.DependencyInjection;

namespace UtilityAi.Compass.Runtime.DI;

/// <summary>
/// Extension methods for registering Compass runtime services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Compass runtime services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional callback to customize <see cref="CompassOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddUtilityAiCompass(
        this IServiceCollection services,
        Action<CompassOptions>? configure = null)
    {
        var options = new CompassOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        return services;
    }
}
