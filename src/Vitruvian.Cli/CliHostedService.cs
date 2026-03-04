using Microsoft.Extensions.Hosting;
using VitruvianAbstractions.Scheduling;
using VitruvianRuntime.Scheduling;

namespace VitruvianCli;

/// <summary>
/// Runs the interactive CLI REPL loop as an <see cref="IHostedService"/> so that the
/// application can participate in the generic host lifecycle alongside other background
/// services (e.g. <see cref="SchedulerService"/>).
/// </summary>
public sealed class CliHostedService : BackgroundService
{
    private readonly RequestProcessor _requestProcessor;
    private readonly IScheduledTaskStore? _taskStore;
    private readonly NaturalLanguageScheduleParser? _scheduleParser;
    private readonly Action _printCommands;
    private readonly Action _printInstalledModules;
    private readonly Func<string, bool, Task> _installModule;
    private readonly Func<string, string, string> _scaffoldModule;
    private readonly IHostApplicationLifetime _lifetime;

    public CliHostedService(
        RequestProcessor requestProcessor,
        IHostApplicationLifetime lifetime,
        Action printCommands,
        Action printInstalledModules,
        Func<string, bool, Task> installModule,
        Func<string, string, string> scaffoldModule,
        IScheduledTaskStore? taskStore = null,
        NaturalLanguageScheduleParser? scheduleParser = null)
    {
        _requestProcessor = requestProcessor;
        _lifetime = lifetime;
        _printCommands = printCommands;
        _printInstalledModules = printInstalledModules;
        _installModule = installModule;
        _scaffoldModule = scaffoldModule;
        _taskStore = taskStore;
        _scheduleParser = scheduleParser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield so the host can finish starting before we block on Console.ReadLine
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = await ReadLineAsync(stoppingToken);

            if (input is null || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                _lifetime.StopApplication();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var trimmed = input.Trim();

            if (string.Equals(trimmed, "/help", StringComparison.OrdinalIgnoreCase))
            {
                _printCommands();
                continue;
            }

            if (string.Equals(trimmed, "/list-modules", StringComparison.OrdinalIgnoreCase))
            {
                _printInstalledModules();
                continue;
            }

            if (string.Equals(trimmed, "/list-tasks", StringComparison.OrdinalIgnoreCase))
            {
                await ListScheduledTasksAsync();
                continue;
            }

            if (trimmed.StartsWith("/cancel-task ", StringComparison.OrdinalIgnoreCase))
            {
                await CancelScheduledTaskAsync(trimmed["/cancel-task ".Length..].Trim());
                continue;
            }

            if (ModuleInstaller.TryParseInstallCommand(input, out var moduleSpec, out var allowUnsigned))
            {
                await _installModule(moduleSpec, allowUnsigned);
                continue;
            }

            if (ModuleInstaller.TryParseNewModuleCommand(input, out var moduleName, out var outputPath))
            {
                Console.WriteLine($"  {_scaffoldModule(moduleName, outputPath)}");
                continue;
            }

            // Check for /schedule command
            if (TryParseScheduleCommand(trimmed, out var scheduleDesc, out var taskRequest))
            {
                await ScheduleTaskAsync(scheduleDesc, taskRequest, stoppingToken);
                continue;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var responseText = await _requestProcessor.ProcessAsync(input, cts.Token);
                Console.WriteLine($"  Response: {responseText}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine("  Error: The request timed out or was canceled.");
            }
        }
    }

    /// <summary>
    /// Parses `/schedule "every 5 minutes" do something` into schedule description and task request.
    /// Format: /schedule "<interval>" <request text>
    /// </summary>
    internal static bool TryParseScheduleCommand(string input, out string scheduleDescription, out string taskRequest)
    {
        scheduleDescription = string.Empty;
        taskRequest = string.Empty;

        if (!input.StartsWith("/schedule ", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = input["/schedule ".Length..].Trim();

        // Expect quoted interval first: "every 5 minutes" then the task
        if (rest.StartsWith('"'))
        {
            var endQuote = rest.IndexOf('"', 1);
            if (endQuote < 0)
                return false;

            scheduleDescription = rest[1..endQuote];
            taskRequest = rest[(endQuote + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(scheduleDescription) && !string.IsNullOrWhiteSpace(taskRequest);
        }

        return false;
    }

    private async Task ScheduleTaskAsync(string scheduleDescription, string taskRequest, CancellationToken ct)
    {
        if (_taskStore is null || _scheduleParser is null)
        {
            Console.WriteLine("  Scheduler is not enabled. Set EnableScheduler = true in options.");
            return;
        }

        var interval = await _scheduleParser.ParseAsync(scheduleDescription, ct);
        if (!interval.HasValue)
        {
            Console.WriteLine($"  Could not parse schedule: \"{scheduleDescription}\". Try e.g. \"every 5 minutes\" or \"daily\".");
            return;
        }

        var task = new ScheduledTask
        {
            Request = taskRequest,
            ScheduleDescription = scheduleDescription,
            RepeatInterval = interval.Value,
            NextRunUtc = DateTimeOffset.UtcNow + interval.Value
        };

        await _taskStore.AddAsync(task, ct);
        Console.WriteLine($"  Scheduled task {task.Id}: \"{taskRequest}\" repeating {scheduleDescription} (every {interval.Value.TotalSeconds}s). Next run: {task.NextRunUtc:u}");
    }

    private async Task ListScheduledTasksAsync()
    {
        if (_taskStore is null)
        {
            Console.WriteLine("  Scheduler is not enabled.");
            return;
        }

        var tasks = await _taskStore.GetAllAsync();
        if (tasks.Count == 0)
        {
            Console.WriteLine("  No scheduled tasks.");
            return;
        }

        Console.WriteLine("  Scheduled tasks:");
        foreach (var t in tasks)
        {
            var status = t.Enabled ? "enabled" : "disabled";
            var repeat = t.RepeatInterval.HasValue ? $"every {t.RepeatInterval.Value.TotalSeconds}s" : "one-shot";
            Console.WriteLine($"    [{t.Id}] \"{t.Request}\" — {repeat} ({status}), next: {t.NextRunUtc:u}, runs: {t.RunCount}");
        }
    }

    private async Task CancelScheduledTaskAsync(string taskId)
    {
        if (_taskStore is null)
        {
            Console.WriteLine("  Scheduler is not enabled.");
            return;
        }

        var removed = await _taskStore.RemoveAsync(taskId);
        Console.WriteLine(removed
            ? $"  Task {taskId} cancelled."
            : $"  Task {taskId} not found.");
    }

    /// <summary>
    /// Reads a line from Console, returning null on EOF or cancellation.
    /// </summary>
    private static async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        try
        {
            // Console.ReadLine() blocks, so run on a thread-pool thread
            return await Task.Run(Console.ReadLine, ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
