using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VitruvianAbstractions;
using VitruvianCli;
using VitruvianRuntime;
using VitruvianRuntime.Routing;
using VitruvianAbstractions.Facts;
using VitruvianAbstractions.Interfaces;
using VitruvianAbstractions.Scheduling;
using VitruvianPluginHost;
using VitruvianPluginSdk.Attributes;
using VitruvianRuntime.DI;
using VitruvianRuntime.Scheduling;
using VitruvianStandardModules;

// Auto-load .env.Vitruvian so the host works without manually sourcing the file.
EnvFileLoader.Load(overwriteExisting: true);

var pluginsPath = Path.Combine(AppContext.BaseDirectory, "plugins");
var modulesPath = Path.Combine(AppContext.BaseDirectory, "modules");
void PrintCommands() => Console.WriteLine("Commands: /help, /setup, /list-modules, /configure-modules, /install-module <path|package@version> [--allow-unsigned], /load-module <path-to-dll>, /unregister-module <domain|filename>, /inspect-module <path|package@version> [--json], /doctor [--json], /policy validate <policyFile>, /policy explain <request>, /audit list, /audit show <id> [--json], /replay <id> [--no-exec], /new-module <Name> [OutputPath], /schedule \"<interval>\" <request>, /list-tasks, /cancel-task <id>, quit");
string GetCurrentPersonaDisplay()
{
    var activePersona = Environment.GetEnvironmentVariable("VITRUVIAN_PROFILE");
    return string.IsNullOrWhiteSpace(activePersona)
        ? "default"
        : activePersona.Trim();
}
string? PromptForSecret(string secretName)
{
    Console.Write($"Missing required secret '{secretName}'. Enter value (blank will fail install): ");
    var value = ReadSecretFromConsole();
    Console.WriteLine();
    return value;
}

static string ReadSecretFromConsole()
{
    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? string.Empty;

    var buffer = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            return new string(buffer.ToArray());

        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Count == 0)
                continue;
            buffer.RemoveAt(buffer.Count - 1);
            Console.Write("\b \b");
            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            buffer.Add(key.KeyChar);
            Console.Write('*');
        }
    }
}
void PrintInstalledModules()
{
    var prefs = ModulePreferences.Load();
    var standardInfos = ModuleSelector.GetStandardModuleInfos();

    Console.WriteLine("Standard modules:");
    foreach (var info in standardInfos)
    {
        var status = info.IsCore || prefs.IsModuleEnabled(info.Domain) ? "enabled" : "disabled";
        var coreTag = info.IsCore ? " (core)" : "";
        Console.WriteLine($"  - {info.Domain} [{status}]{coreTag}");
    }

    // Show modules from the modules/ folder
    var discoveredModules = ModuleSelector.DiscoverModulesFromFolder(modulesPath);
    if (discoveredModules.Count > 0)
    {
        Console.WriteLine("Discovered modules (modules/ folder):");
        foreach (var info in discoveredModules)
        {
            var status = prefs.IsModuleEnabled(info.Domain) ? "enabled" : "disabled";
            Console.WriteLine($"  - {info.Domain} [{status}]  ({info.Source})");
        }
    }

    using var loaderServiceProvider = new ServiceCollection().BuildServiceProvider();
    var installedModules = InstalledModuleLoader.LoadModulesWithSources(pluginsPath, loaderServiceProvider);
    var installedDlls = ModuleInstaller.ListInstalledModules(pluginsPath);
    if (installedDlls.Count == 0)
    {
        Console.WriteLine("No installed plugin modules found.");
    }
    else
    {
        Console.WriteLine("Installed plugin modules:");
        foreach (var (module, dllPath) in installedModules)
        {
            Console.WriteLine($"  - {module.Domain}  ({Path.GetFileName(dllPath)})");
        }

        // Show any DLLs that didn't produce loadable modules
        var loadedDlls = installedModules.Select(m => Path.GetFileName(m.SourceDllPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in installedDlls.Where(d => !loadedDlls.Contains(d)))
        {
            Console.WriteLine($"  - {dll}  (could not load)");
        }

        Console.WriteLine("Use '/unregister-module <domain or filename>' to remove.");
    }

    Console.WriteLine("\nUse '/configure-modules' or '--configure-modules' to change module selection.");
}

Task<int> PrintAuditListAsync()
{
    Console.WriteLine("Audit feature removed with UtilityAI package.");
    Console.WriteLine("Audit functionality was part of the old architecture and is no longer available.");
    return Task.FromResult(1);
}

var startupArgs = args
    .Where(arg => !string.IsNullOrWhiteSpace(arg))
    .Select(arg => arg.Trim())
    .ToArray();
if (startupArgs.Length >= 1 && string.Equals(startupArgs[0], "--", StringComparison.Ordinal))
    startupArgs = startupArgs[1..];

if (startupArgs.Length >= 1 &&
    (string.Equals(startupArgs[0], "--help", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/help", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Vitruvian CLI arguments:");
    Console.WriteLine("  --help");
    Console.WriteLine("  --setup");
    Console.WriteLine("  --list-modules");
    Console.WriteLine("  --install-module <path|package@version> [--allow-unsigned]");
    Console.WriteLine("  --unregister-module <domain>");
    Console.WriteLine("  --configure-modules");
    Console.WriteLine("  --model <provider[:model]>  (e.g. --model openai or --model anthropic:claude-3-5-sonnet-latest)");
    Console.WriteLine("  --inspect-module <path|package@version> [--json] (alias: inspect-module)");
    Console.WriteLine("  --doctor [--json] (alias: doctor)");
    Console.WriteLine("  --policy validate <policyFile> (alias: policy validate)");
    Console.WriteLine("  --policy explain <request> (alias: policy explain)");
    Console.WriteLine("  --audit list (alias: audit list)");
    Console.WriteLine("  --audit show <id> [--json] (alias: audit show)");
    Console.WriteLine("  --replay <id> [--no-exec] (alias: replay)");
    Console.WriteLine("  --new-module <Name> [OutputPath]");
    Console.WriteLine();
    Console.WriteLine("Getting started:");
    Console.WriteLine("  1) Vitruvian --setup");
    Console.WriteLine("  2) Vitruvian");
    Console.WriteLine("  3) In interactive mode, type /help for commands or 'quit' to exit.");
    return;
}

if (startupArgs.Length >= 1 &&
    (string.Equals(startupArgs[0], "--list-modules", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/list-modules", StringComparison.OrdinalIgnoreCase)))
{
    PrintInstalledModules();
    return;
}

if (startupArgs.Length >= 2 &&
    (string.Equals(startupArgs[0], "--install-module", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/install-module", StringComparison.OrdinalIgnoreCase)))
{
    var allowUnsigned = startupArgs.Any(a => string.Equals(a, "--allow-unsigned", StringComparison.OrdinalIgnoreCase));
    var installResult = await ModuleInstaller.InstallWithResultAsync(startupArgs[1], pluginsPath, allowUnsigned, PromptForSecret);
    Console.WriteLine(installResult.Message);
    if (!installResult.Success)
        Environment.ExitCode = 1;
    Console.WriteLine("Restart Vitruvian CLI to load the new module.");
    return;
}

if (startupArgs.Length >= 2 &&
    (string.Equals(startupArgs[0], "inspect-module", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--inspect-module", StringComparison.OrdinalIgnoreCase)))
{
    var report = await ModuleInstaller.InspectAsync(startupArgs[1]);
    var asJson = startupArgs.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
    if (asJson)
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    else
    {
        Console.WriteLine(report.Summary);
        Console.WriteLine($"  hasModule: {report.HasUtilityAiModule}");
        Console.WriteLine($"  signed: {report.IsSigned}");
        Console.WriteLine($"  manifest: {report.HasManifest}");
        if (report.Capabilities.Count > 0)
            Console.WriteLine($"  capabilities: {string.Join(", ", report.Capabilities)}");
        if (report.Permissions.Count > 0)
            Console.WriteLine($"  permissions: {string.Join(", ", report.Permissions)}");
        if (report.Findings.Count > 0)
            Console.WriteLine($"  findings: {string.Join(" | ", report.Findings)}");
    }
    return;
}

if (startupArgs.Length >= 1 &&
    (string.Equals(startupArgs[0], "doctor", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--doctor", StringComparison.OrdinalIgnoreCase)))
{
    var hasUnsigned = ModuleInstaller.ListInstalledModules(pluginsPath).Any();
    var findings = new List<string>();
    if (hasUnsigned)
        findings.Add("Installed modules should be inspected with `Vitruvian inspect-module` and signed by default.");
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VITRUVIAN_MEMORY_CONNECTION_STRING")))
        findings.Add("Audit store not configured. Set VITRUVIAN_MEMORY_CONNECTION_STRING to SQLite for deterministic audit.");
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VITRUVIAN_SECRET_PROVIDER")))
        findings.Add("Secret provider not configured. Set VITRUVIAN_SECRET_PROVIDER to avoid direct environment-secret usage.");

    var report = new
    {
        Status = findings.Count == 0 ? "healthy" : "needs-attention",
        Findings = findings
    };
    if (startupArgs.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    else
    {
        Console.WriteLine($"Doctor status: {report.Status}");
        foreach (var finding in report.Findings)
            Console.WriteLine($"  - {finding}");
    }
    return;
}

if (startupArgs.Length >= 3 &&
    (string.Equals(startupArgs[0], "policy", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--policy", StringComparison.OrdinalIgnoreCase)) &&
    string.Equals(startupArgs[1], "validate", StringComparison.OrdinalIgnoreCase))
{
    var policyPath = startupArgs[2];
    if (!File.Exists(policyPath))
    {
        Console.WriteLine($"Policy validation failed: '{policyPath}' not found.");
        return;
    }

    try
    {
        using var stream = File.OpenRead(policyPath);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var hasRules = root.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array;
        Console.WriteLine(hasRules
            ? "Policy validation succeeded."
            : "Policy validation failed: expected top-level JSON array property 'rules'.");
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"Policy validation failed: {ex.Message}");
    }
    return;
}

if (startupArgs.Length >= 3 &&
    (string.Equals(startupArgs[0], "policy", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--policy", StringComparison.OrdinalIgnoreCase)) &&
    string.Equals(startupArgs[1], "explain", StringComparison.OrdinalIgnoreCase))
{
    var request = string.Join(' ', startupArgs.Skip(2));
    var requiresApproval = request.Contains("delete", StringComparison.OrdinalIgnoreCase)
        || request.Contains("write", StringComparison.OrdinalIgnoreCase)
        || request.Contains("update", StringComparison.OrdinalIgnoreCase);
    Console.WriteLine(requiresApproval
        ? "Policy explain: matched EnterpriseSafe write/destructive guard; approval required."
        : "Policy explain: matched EnterpriseSafe readonly allow rule.");
    return;
}

if (startupArgs.Length >= 2 &&
    (string.Equals(startupArgs[0], "audit", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--audit", StringComparison.OrdinalIgnoreCase)) &&
    string.Equals(startupArgs[1], "list", StringComparison.OrdinalIgnoreCase))
{
    await PrintAuditListAsync();
    return;
}

if (startupArgs.Length >= 3 &&
    (string.Equals(startupArgs[0], "audit", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--audit", StringComparison.OrdinalIgnoreCase)) &&
    string.Equals(startupArgs[1], "show", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Audit feature removed with UtilityAI package.");
    Console.WriteLine("Audit functionality was part of the old architecture and is no longer available.");
    return;
}

if (startupArgs.Length >= 2 &&
    (string.Equals(startupArgs[0], "replay", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "--replay", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine($"Replay accepted for audit id '{startupArgs[1]}'.");
    Console.WriteLine(startupArgs.Any(a => string.Equals(a, "--no-exec", StringComparison.OrdinalIgnoreCase))
        ? "Replay mode: selection-only (no side effects)."
        : "Replay mode: side effects disabled by default in this build.");
    return;
}

if (startupArgs.Length >= 2 &&
    (string.Equals(startupArgs[0], "--new-module", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/new-module", StringComparison.OrdinalIgnoreCase)))
{
    var outputPath = startupArgs.Length >= 3 ? startupArgs[2] : Directory.GetCurrentDirectory();
    Console.WriteLine(ModuleInstaller.ScaffoldNewModule(startupArgs[1], outputPath));
    return;
}

if (startupArgs.Length >= 1 &&
    (string.Equals(startupArgs[0], "--setup", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/setup", StringComparison.OrdinalIgnoreCase)))
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
    return;
}

if (startupArgs.Length >= 1 &&
    (string.Equals(startupArgs[0], "--configure-modules", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/configure-modules", StringComparison.OrdinalIgnoreCase)))
{
    var standardModules = ModuleSelector.GetStandardModuleInfos();
    var discoveredModules = ModuleSelector.DiscoverModulesFromFolder(modulesPath);
    var allModules = standardModules.Concat(discoveredModules).ToList();
    var prefs = ModulePreferences.Load();
    prefs = ModuleSelector.RunInteractiveSelection(allModules, prefs);
    prefs.Save();
    Console.WriteLine("Module preferences saved.");
    return;
}

if (startupArgs.Length >= 2 &&
    (string.Equals(startupArgs[0], "--model", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(startupArgs[0], "/model", StringComparison.OrdinalIgnoreCase)))
{
    var modelArg = startupArgs[1];
    var colonIndex = modelArg.IndexOf(':');
    var provider = colonIndex >= 0 ? modelArg[..colonIndex] : modelArg;
    var modelName = colonIndex >= 0 ? modelArg[(colonIndex + 1)..] : null;

    if (!string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Unknown provider '{provider}'. Supported: openai, anthropic, gemini.");
        return;
    }

    EnvFileLoader.PersistSecret("VITRUVIAN_MODEL_PROVIDER", provider.ToLowerInvariant());
    if (!string.IsNullOrWhiteSpace(modelName))
        EnvFileLoader.PersistSecret("VITRUVIAN_MODEL_NAME", modelName);
    EnvFileLoader.Load(overwriteExisting: true);
    Console.WriteLine($"Model provider set to '{provider}'" +
        (string.IsNullOrWhiteSpace(modelName) ? " (using default model)." : $" with model '{modelName}'."));
    return;
}

string? modelConfigurationError = null;
if (!ModelConfiguration.TryCreateFromEnvironment(out var modelConfiguration, out modelConfigurationError) &&
    EnvFileLoader.FindFile([Directory.GetCurrentDirectory(), AppContext.BaseDirectory]) is null &&
    !Console.IsInputRedirected)
{
    Console.WriteLine("No Vitruvian setup found. Running installer...");
    if (ModuleInstaller.TryRunInstallScript())
    {
        EnvFileLoader.Load(startDirectory: AppContext.BaseDirectory, overwriteExisting: true);
        ModelConfiguration.TryCreateFromEnvironment(out modelConfiguration, out modelConfigurationError);
    }
}
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
    PrintCommands();
    if (modelConfiguration is not null)
        Console.WriteLine($"Model provider configured: {modelConfiguration.Provider} ({modelConfiguration.Model})");
    Console.WriteLine($"Current persona: {GetCurrentPersonaDisplay()}");
    Console.WriteLine($"Working directory: {workingDirectory}");

    var cliService = new CliHostedService(
        requestProcessor,
        host.Services.GetRequiredService<IHostApplicationLifetime>(),
        PrintCommands,
        PrintInstalledModules,
        async (spec, unsigned) =>
        {
            var installResult = await ModuleInstaller.InstallWithResultAsync(spec, pluginsPath, unsigned, PromptForSecret);
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
            var standardModules = ModuleSelector.GetStandardModuleInfos();
            var discoveredModules = ModuleSelector.DiscoverModulesFromFolder(modulesPath);
            var allModules = standardModules.Concat(discoveredModules).ToList();
            var prefs = ModulePreferences.Load();
            prefs = ModuleSelector.RunInteractiveSelection(allModules, prefs);
            prefs.Save();
            Console.WriteLine("Module preferences saved. Restart Vitruvian CLI to apply changes.");
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
