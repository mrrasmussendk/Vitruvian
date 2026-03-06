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
        // Validate environment first
        var validationResult = EnvironmentValidator.ValidateEnvironment();

        if (ModelConfiguration.TryCreateFromEnvironment(out var modelConfiguration, out var configError))
        {
            // Even if model is configured, show warnings if any
            if (validationResult.Warnings.Count > 0)
            {
                foreach (var warning in validationResult.Warnings)
                {
                    if (!warning.Contains(".env.Vitruvian file found")) // Skip this one if already configured
                        Console.WriteLine($"⚠ {warning}");
                }
            }
            return (modelConfiguration, configError);
        }

        // Check if there's an env file somewhere
        if (EnvFileLoader.FindFile([Directory.GetCurrentDirectory(), AppContext.BaseDirectory]) is not null)
            return (modelConfiguration, configError);

        // Don't run interactive onboarding when input is piped
        if (Console.IsInputRedirected)
            return (modelConfiguration, configError);

        PrintWelcomeBanner();

        Console.WriteLine("No Vitruvian configuration found. Starting guided setup...");
        Console.WriteLine();

        // Show validation issues
        if (!validationResult.IsValid || validationResult.Warnings.Count > 0)
        {
            EnvironmentValidator.PrintValidationResults(validationResult);
        }

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
                var provider = modelConfiguration.Provider;
                var model = modelConfiguration.Model;
                var persona = ConsoleHelper.GetCurrentPersonaDisplay();

                // Compute dynamic box width based on content
                var contentWidth = Math.Max(
                    Math.Max("  ✓ Setup complete!".Length, $"  Provider: {provider}".Length),
                    Math.Max($"  Model: {model}".Length, $"  Persona: {persona}".Length));
                var boxWidth = contentWidth + 4; // padding

                var border = new string('─', boxWidth);
                Console.WriteLine($"┌{border}┐");
                Console.WriteLine($"│{"  ✓ Setup complete!".PadRight(boxWidth)}│");
                Console.WriteLine($"│{$"  Provider: {provider}".PadRight(boxWidth)}│");
                Console.WriteLine($"│{$"  Model: {model}".PadRight(boxWidth)}│");
                Console.WriteLine($"│{$"  Persona: {persona}".PadRight(boxWidth)}│");
                Console.WriteLine($"└{border}┘");
            }
            else
            {
                Console.WriteLine("Setup completed but model configuration could not be resolved.");
                if (!string.IsNullOrWhiteSpace(configError))
                    Console.WriteLine($"  Hint: {configError}");
                Console.WriteLine("  Run 'Vitruvian --setup' to try again or 'Vitruvian --model <provider>' to configure manually.");
            }
            Console.WriteLine();

            // After basic setup, prompt for module selection
            ConfigureModules();
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

    /// <summary>
    /// Runs interactive module configuration, allowing users to select which modules to enable.
    /// </summary>
    public static void ConfigureModules()
    {
        var modulesPath = Path.Combine(AppContext.BaseDirectory, "modules");
        var preferences = ModulePreferences.Load();

        // Get standard modules
        var standardModules = ModuleSelector.GetStandardModuleInfos();

        // Discover modules from modules/ folder
        var discoveredModules = ModuleSelector.DiscoverModulesFromFolder(modulesPath);

        // Combine all modules
        var allModules = standardModules.Concat(discoveredModules).ToList();

        if (allModules.Count == 0)
        {
            Console.WriteLine("No modules found to configure.");
            return;
        }

        Console.WriteLine("Would you like to configure which modules to enable? (Y/n): ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (response == "n" || response == "no")
        {
            Console.WriteLine("Skipping module configuration. All built-in modules will be enabled by default.");
            Console.WriteLine("Discovered modules will be disabled. Run 'Vitruvian --configure-modules' to change this.");
            Console.WriteLine();
            return;
        }

        // Run interactive selection
        var updatedPreferences = ModuleSelector.RunInteractiveSelection(allModules, preferences);
        updatedPreferences.Save();

        Console.WriteLine("✓ Module preferences saved.");
        Console.WriteLine();
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
