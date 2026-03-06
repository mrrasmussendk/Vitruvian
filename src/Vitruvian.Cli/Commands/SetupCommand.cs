namespace VitruvianCli.Commands;

/// <summary>
/// Runs the guided setup flow, including the install script and model configuration.
/// </summary>
public sealed class SetupCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 1 &&
        (string.Equals(args[0], "--setup", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/setup", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        var setupCompleted = ModuleInstaller.TryRunInstallScript();
        if (setupCompleted)
        {
            EnvFileLoader.Load(startDirectory: AppContext.BaseDirectory, overwriteExisting: true);
            Console.WriteLine($"Vitruvian setup complete. Current persona: {GetCurrentPersonaDisplay()}.");
            if (ModelConfiguration.TryCreateFromEnvironment(out var setupModelConfig, out var setupError) && setupModelConfig is not null)
                Console.WriteLine($"Model provider configured: {setupModelConfig.Provider} ({setupModelConfig.Model})");
            else if (!string.IsNullOrWhiteSpace(setupError))
                Console.WriteLine($"Model configuration warning: {setupError}");
        }
        else
        {
            Console.WriteLine("Vitruvian setup script could not be started. Ensure scripts/install.sh or scripts/install.ps1 exists next to the app.");
        }

        return Task.FromResult(setupCompleted ? 0 : 1);
    }

    internal static string GetCurrentPersonaDisplay()
    {
        var activePersona = Environment.GetEnvironmentVariable("VITRUVIAN_PROFILE");
        return string.IsNullOrWhiteSpace(activePersona)
            ? "default"
            : activePersona.Trim();
    }
}
