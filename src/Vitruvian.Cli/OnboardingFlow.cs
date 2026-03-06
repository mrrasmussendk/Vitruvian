using VitruvianCli.Commands;

namespace VitruvianCli;

/// <summary>
/// Manages the first-run onboarding experience for new Vitruvian users.
/// Detects whether setup has been completed and guides users through configuration.
/// </summary>
public static class OnboardingFlow
{
    /// <summary>
    /// Checks whether model configuration exists and runs the onboarding flow if needed.
    /// Returns the resolved <see cref="ModelConfiguration"/> (which may be null if setup was skipped).
    /// </summary>
    public static (ModelConfiguration? Configuration, string? Error) EnsureConfigured()
    {
        if (ModelConfiguration.TryCreateFromEnvironment(out var modelConfiguration, out var configError))
            return (modelConfiguration, configError);

        // Check if there's an env file somewhere
        if (EnvFileLoader.FindFile([Directory.GetCurrentDirectory(), AppContext.BaseDirectory]) is not null)
            return (modelConfiguration, configError);

        // Don't run interactive onboarding when input is piped
        if (Console.IsInputRedirected)
            return (modelConfiguration, configError);

        PrintWelcomeBanner();

        Console.WriteLine("No Vitruvian configuration found. Starting guided setup...");
        Console.WriteLine();
        Console.WriteLine("This will:");
        Console.WriteLine("  1. Configure your AI model provider (OpenAI, Anthropic, or Gemini)");
        Console.WriteLine("  2. Set up API keys and preferences");
        Console.WriteLine("  3. Select which modules to enable");
        Console.WriteLine();

        if (ModuleInstaller.TryRunInstallScript())
        {
            EnvFileLoader.Load(startDirectory: AppContext.BaseDirectory, overwriteExisting: true);
            ModelConfiguration.TryCreateFromEnvironment(out modelConfiguration, out configError);

            Console.WriteLine();
            if (modelConfiguration is not null)
            {
                Console.WriteLine("┌─────────────────────────────────────────────┐");
                Console.WriteLine("│  ✓ Setup complete!                          │");
                Console.WriteLine($"│  Provider: {modelConfiguration.Provider,-33}│");
                Console.WriteLine($"│  Model: {modelConfiguration.Model,-36}│");
                Console.WriteLine($"│  Persona: {SetupCommand.GetCurrentPersonaDisplay(),-34}│");
                Console.WriteLine("└─────────────────────────────────────────────┘");
            }
            else
            {
                Console.WriteLine("Setup completed but model configuration could not be resolved.");
                if (!string.IsNullOrWhiteSpace(configError))
                    Console.WriteLine($"  Hint: {configError}");
                Console.WriteLine("  Run 'Vitruvian --setup' to try again or 'Vitruvian --model <provider>' to configure manually.");
            }
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("Setup script could not be started.");
            Console.WriteLine("To configure manually:");
            Console.WriteLine("  Vitruvian --model openai          Set your model provider");
            Console.WriteLine("  Vitruvian --configure-modules     Choose which modules to enable");
            Console.WriteLine();
        }

        return (modelConfiguration, configError);
    }

    private static void PrintWelcomeBanner()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║                                               ║");
        Console.WriteLine("║   Welcome to Vitruvian AI Assistant            ║");
        Console.WriteLine("║                                               ║");
        Console.WriteLine("║   A modular, GOAP-driven AI framework          ║");
        Console.WriteLine("║   that plans before it acts.                   ║");
        Console.WriteLine("║                                               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
