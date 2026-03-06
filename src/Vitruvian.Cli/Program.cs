using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VitruvianAbstractions;
using VitruvianCli;
using VitruvianCli.Commands;
using VitruvianRuntime;
using VitruvianRuntime.Routing;
using VitruvianAbstractions.Interfaces;
using VitruvianAbstractions.Scheduling;
using VitruvianPluginHost;
using VitruvianRuntime.DI;
using VitruvianRuntime.Scheduling;
using VitruvianStandardModules;

// Auto-load .env.Vitruvian so the host works without manually sourcing the file.
EnvFileLoader.Load(overwriteExisting: true);

var pluginsPath = Path.Combine(AppContext.BaseDirectory, "plugins");
var modulesPath = Path.Combine(AppContext.BaseDirectory, "modules");

// --- Handle startup commands (run once and exit) ---
var startupArgs = args
    .Where(arg => !string.IsNullOrWhiteSpace(arg))
    .Select(arg => arg.Trim())
    .ToArray();
if (startupArgs.Length >= 1 && string.Equals(startupArgs[0], "--", StringComparison.Ordinal))
    startupArgs = startupArgs[1..];

var commandRouter = CommandRouter.CreateDefault(pluginsPath, modulesPath);
if (commandRouter.TryRoute(startupArgs, out var command))
{
    await command.ExecuteAsync(startupArgs);
    return;
}

// --- Onboarding: ensure model configuration is present ---
var (modelConfiguration, modelConfigurationError) = OnboardingFlow.EnsureConfigured();
if (modelConfiguration is null && !string.IsNullOrWhiteSpace(modelConfigurationError))
    Console.WriteLine($"Model configuration warning: {modelConfigurationError}");

var builder = Host.CreateApplicationBuilder(args);
var memoryConnectionString = Environment.GetEnvironmentVariable("VITRUVIAN_MEMORY_CONNECTION_STRING");

// Set up working directory - defaults to "Vitruvian-workspace" in user's home directory
var workingDirectory = Environment.GetEnvironmentVariable("VITRUVIAN_WORKING_DIRECTORY");
if (string.IsNullOrWhiteSpace(workingDirectory))
{
    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    workingDirectory = Path.Combine(homeDir, "Vitruvian-workspace");
}

// Ensure working directory exists
if (!Directory.Exists(workingDirectory))
{
    Directory.CreateDirectory(workingDirectory);
    Console.WriteLine($"Created working directory: {workingDirectory}");
}

builder.Services.AddUtilityAiVitruvian(opts =>
{
    opts.EnableGovernanceFinalizer = true;
    opts.EnableHitl = false;
    opts.EnableScheduler = true;
    opts.MemoryConnectionString = memoryConnectionString;
    opts.WorkingDirectory = workingDirectory;
});

// Register the host-level model client so plugins receive it via DI.
// The concrete provider (OpenAI, Anthropic, Gemini) is chosen by env config.
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var approvalGate = new VitruvianHitl.ConsoleApprovalGate(timeout: TimeSpan.FromSeconds(30));
IModelClient? modelClient = modelConfiguration is not null
    ? ModelClientFactory.Create(modelConfiguration, httpClient, approvalGate)
    : null;
if (modelClient is not null)
    builder.Services.AddSingleton<IModelClient>(modelClient);
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();

// Load module preferences to determine which standard modules to enable.
var modulePreferences = ModulePreferences.Load();

// Register modules — core modules are always registered, optional modules respect preferences.
builder.Services.AddSingleton<IVitruvianModule>(sp =>
    new ConversationModule(sp.GetService<IModelClient>()));
if (modulePreferences.IsModuleEnabled("file-operations"))
    builder.Services.AddSingleton<IVitruvianModule>(sp =>
        new FileOperationsModule(sp.GetService<IModelClient>(), workingDirectory));
if (modulePreferences.IsModuleEnabled("web-search"))
    builder.Services.AddSingleton<IVitruvianModule>(sp =>
        new WebSearchModule(sp.GetService<IModelClient>()));
if (modulePreferences.IsModuleEnabled("summarization"))
    builder.Services.AddSingleton<IVitruvianModule>(sp =>
        new SummarizationModule(sp.GetService<IModelClient>()));
if (modulePreferences.IsModuleEnabled("shell-command"))
    builder.Services.AddSingleton<IVitruvianModule>(sp =>
        new ShellCommandModule(
            sp.GetService<IModelClient>(),
            workingDirectory,
        commandRunner: sp.GetRequiredService<ICommandRunner>()));

// Register module router with configuration options
builder.Services.AddSingleton<ModuleRouter>(sp =>
{
    var options = sp.GetService<Microsoft.Extensions.Options.IOptions<VitruvianOptions>>()?.Value?.Router ?? new RouterOptions();
    return new ModuleRouter(sp.GetService<IModelClient>(), options);
});

var host = builder.Build();

// Create and configure the request processor
var router = host.Services.GetRequiredService<ModuleRouter>();
var requestProcessor = new RequestProcessor(host, router, modelClient, approvalGate);

// Register all modules with the router
foreach (var module in host.Services.GetServices<IVitruvianModule>())
{
    requestProcessor.RegisterModule(module);
}

// Track which DLL each plugin module was loaded from and whether it should be deleted on unregister.
var pluginSources = new Dictionary<string, (string SourceDllPath, bool DeleteOnUnregister)>();
foreach (var (module, sourceDllPath) in InstalledModuleLoader.LoadModulesWithSources(pluginsPath, host.Services))
{
    requestProcessor.RegisterModule(module);
    pluginSources[module.Domain] = (sourceDllPath, DeleteOnUnregister: true);
}

// Load user-selectable modules from the modules/ folder, respecting preferences.
if (Directory.Exists(modulesPath))
{
    foreach (var (module, sourceDllPath) in ModuleSelector.LoadEnabledModules(modulesPath, modulePreferences, host.Services))
    {
        requestProcessor.RegisterModule(module);
        pluginSources[module.Domain] = (sourceDllPath, DeleteOnUnregister: false);
    }
}

var discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
var discordChannelId = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
var webSocketUrl = Environment.GetEnvironmentVariable("VITRUVIAN_WEBSOCKET_URL");
var webSocketPublicUrl = Environment.GetEnvironmentVariable("VITRUVIAN_WEBSOCKET_PUBLIC_URL");
var webSocketDomain = Environment.GetEnvironmentVariable("VITRUVIAN_WEBSOCKET_DOMAIN");

// Resolve scheduler services (registered by AddUtilityAiVitruvian when EnableScheduler is true)
var vitruvianOptions = host.Services.GetRequiredService<VitruvianOptions>();
var taskStore = host.Services.GetService<IScheduledTaskStore>();
var scheduleParser = host.Services.GetService<NaturalLanguageScheduleParser>();

// Register the scheduler background service when enabled
if (vitruvianOptions.EnableScheduler && taskStore is not null)
{
    var schedulerService = new SchedulerService(
        taskStore,
        requestProcessor.ProcessAsync,
        vitruvianOptions,
        msg => Console.WriteLine(msg));
    _ = schedulerService.StartAsync(CancellationToken.None);
}

if (!string.IsNullOrWhiteSpace(webSocketUrl))
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var bridge = new WebSocketChannelBridge(webSocketUrl, webSocketPublicUrl, webSocketDomain);
    await bridge.RunAsync(async (request, token) =>
    {
        var response = await requestProcessor.ProcessAsync(request, token);
        return response;
    }, cts.Token);
}
else if (!string.IsNullOrWhiteSpace(discordToken) && !string.IsNullOrWhiteSpace(discordChannelId))
{
    Console.WriteLine("Vitruvian CLI started in Discord mode.");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var bridge = new DiscordChannelBridge(httpClient, discordToken, discordChannelId);
    await bridge.RunAsync(async (message, token) =>
    {
        var response = await requestProcessor.ProcessAsync(message, token);
        return response;
    }, cts.Token);
}
else
{
    Console.WriteLine("Vitruvian CLI started. Type a request (or 'quit' to exit):");
    ConsoleHelper.PrintCommands();
    if (modelConfiguration is not null)
        Console.WriteLine($"Model provider configured: {modelConfiguration.Provider} ({modelConfiguration.Model})");
    Console.WriteLine($"Current persona: {ConsoleHelper.GetCurrentPersonaDisplay()}");
    Console.WriteLine($"Working directory: {workingDirectory}");

    var listModulesCmd = new ListModulesCommand(pluginsPath, modulesPath);
    var configureModulesCmd = new ConfigureModulesCommand(modulesPath);

    var cliService = new CliHostedService(
        requestProcessor,
        host.Services.GetRequiredService<IHostApplicationLifetime>(),
        ConsoleHelper.PrintCommands,
        listModulesCmd.PrintInstalledModules,
        async (spec, unsigned) =>
        {
            var installResult = await ModuleInstaller.InstallWithResultAsync(spec, pluginsPath, unsigned, secretName =>
            {
                Console.Write($"Missing required secret '{secretName}'. Enter value (blank will fail install): ");
                var value = ConsoleHelper.ReadSecretFromConsole();
                Console.WriteLine();
                return value;
            });
            Console.WriteLine($"  {installResult.Message}");
            if (installResult.Success)
            {
                var loaded = InstalledModuleLoader.LoadModulesWithSources(pluginsPath, host.Services);
                var registered = 0;
                foreach (var (module, sourceDllPath) in loaded)
                {
                    if (!requestProcessor.IsModuleRegistered(module.Domain))
                    {
                        requestProcessor.RegisterModule(module);
                        pluginSources[module.Domain] = (sourceDllPath, DeleteOnUnregister: true);
                        registered++;
                    }
                }

                Console.WriteLine(registered > 0
                    ? $"  {registered} new module(s) loaded and ready to use."
                    : "  Module(s) loaded and ready to use.");
            }
        },
        modulePath =>
        {
            if (string.IsNullOrWhiteSpace(modulePath))
                return Task.FromResult("Load failed: provide a path to a module DLL.");

            var normalizedPath = Path.GetFullPath(modulePath.Trim());
            if (!File.Exists(normalizedPath))
                return Task.FromResult($"Load failed: '{normalizedPath}' does not exist.");
            if (!normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult("Load failed: only .dll files can be loaded with /load-module.");

            try
            {
                var assembly = Assembly.LoadFrom(normalizedPath);
                var loadedModules = InstalledModuleLoader.CreateModulesFromAssembly(assembly, host.Services);
                if (loadedModules.Count == 0)
                    return Task.FromResult($"Load failed: '{Path.GetFileName(normalizedPath)}' does not export an IVitruvianModule.");

                var registeredCount = 0;
                var replacedCount = 0;
                foreach (var module in loadedModules)
                {
                    var wasRegistered = requestProcessor.IsModuleRegistered(module.Domain);
                    requestProcessor.RegisterModule(module);
                    pluginSources[module.Domain] = (normalizedPath, DeleteOnUnregister: false);
                    if (wasRegistered)
                        replacedCount++;
                    else
                        registeredCount++;
                }

                var summary = $"Loaded {registeredCount} module(s) from '{Path.GetFileName(normalizedPath)}'";
                return Task.FromResult(
                    replacedCount > 0
                        ? $"{summary} and replaced {replacedCount} existing registration(s). Use /unregister-module <domain> to remove debug modules."
                        : $"{summary}. Use /unregister-module <domain> to remove debug modules.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Load failed: {ex.Message}");
            }
        },
        domain =>
        {
            // If the user typed a DLL filename instead of a domain, resolve it
            if (!requestProcessor.IsModuleRegistered(domain))
            {
                var match = pluginSources.FirstOrDefault(kvp =>
                    string.Equals(Path.GetFileName(kvp.Value.SourceDllPath), domain, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(kvp.Value.SourceDllPath), domain, StringComparison.OrdinalIgnoreCase));
                if (match.Key is not null)
                    domain = match.Key;
            }

            if (!requestProcessor.UnregisterModule(domain))
                return false;

            // If the module came from a plugin DLL, delete the DLL so it is not reloaded on restart
            if (pluginSources.TryGetValue(domain, out var dllPath))
            {
                pluginSources.Remove(domain);

                // Unregister any other modules that were loaded from the same DLL
                var coLocated = pluginSources
                    .Where(kvp => string.Equals(kvp.Value.SourceDllPath, dllPath.SourceDllPath, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var co in coLocated)
                {
                    if (!requestProcessor.UnregisterModule(co))
                        Console.WriteLine($"  [WARN] Co-located module '{co}' was already unregistered.");
                    pluginSources.Remove(co);
                }

                if (!dllPath.DeleteOnUnregister)
                    return true;

                try
                {
                    File.Delete(dllPath.SourceDllPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [WARN] Could not delete plugin DLL '{Path.GetFileName(dllPath.SourceDllPath)}': {ex.Message}");
                }
            }

            return true;
        },
        ModuleInstaller.ScaffoldNewModule,
        taskStore,
        scheduleParser,
        configureModules: () =>
        {
            configureModulesCmd.RunConfigureModules();
            Console.WriteLine("Restart Vitruvian CLI to apply changes.");
        });

    // Register the CLI service as a hosted service and run the host.
    // The host manages the CLI lifecycle alongside any other background services (e.g. scheduler).
    await cliService.StartAsync(CancellationToken.None);

    // Wait until the application is signalled to stop (by CliHostedService calling StopApplication or Ctrl+C)
    var tcs = new TaskCompletionSource();
    host.Services.GetRequiredService<IHostApplicationLifetime>()
        .ApplicationStopping.Register(() => tcs.TrySetResult());
    await tcs.Task;

    await cliService.StopAsync(CancellationToken.None);
}

Console.WriteLine("Vitruvian CLI stopped.");
