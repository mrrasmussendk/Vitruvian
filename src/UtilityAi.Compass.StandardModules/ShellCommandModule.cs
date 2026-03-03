using System.Diagnostics;
using System.Text.Json;
using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Shell command module implementing ICompassModule.
/// Executes a guarded allowlist of commands in the configured working directory.
/// </summary>
public sealed class ShellCommandModule : ICompassModule
{
    private const string CommandParsingPrompt = """
Determine which command should be executed from the user request.
Return ONLY valid JSON in this format: {"command":"command-name","args":["arg1","arg2"]}
- command: executable name only, no shell wrappers
- args: argument array (empty array if no args)
""";
    private const int MaxOutputLength = 8_000;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] DefaultAllowedCommands =
    [
        "cat",
        "date",
        "dotnet",
        "echo",
        "git",
        "ls",
        "pwd",
        "uname",
        "whoami"
    ];

    private readonly IModelClient? _modelClient;
    private readonly string _workingDirectory;
    private readonly HashSet<string> _allowedCommands;

    public string Domain => "shell-command";
    public string Description => "Execute approved shell/command-line operations for development workflows (e.g., dotnet --version, git status, ls)";

    public ShellCommandModule(
        IModelClient? modelClient = null,
        string? workingDirectory = null,
        IEnumerable<string>? allowedCommands = null)
    {
        _modelClient = modelClient;
        _workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        _allowedCommands = new HashSet<string>(
            allowedCommands ?? LoadAllowedCommandsFromEnvironment(),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        var invocation = await DetermineCommandInvocationAsync(request, ct);
        if (string.IsNullOrWhiteSpace(invocation.Command))
            return "Could not determine a command to run.";

        if (!_allowedCommands.Contains(invocation.Command))
        {
            var allowedList = string.Join(", ", _allowedCommands.OrderBy(static c => c));
            return $"Command '{invocation.Command}' is not allowed. Allowed commands: {allowedList}";
        }

        return await ExecuteCommandAsync(invocation, ct);
    }

    private async Task<CommandInvocation> DetermineCommandInvocationAsync(string request, CancellationToken ct)
    {
        if (_modelClient is null)
            return ParseCommandLine(request);

        try
        {
            var response = await _modelClient.CompleteAsync(
                systemMessage: CommandParsingPrompt,
                userMessage: $"Analyze this shell/command request: {request}",
                cancellationToken: ct);

            return ParseCommandResponse(response);
        }
        catch
        {
            return ParseCommandLine(request);
        }
    }

    private static CommandInvocation ParseCommandResponse(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            var command = root.GetProperty("command").GetString() ?? string.Empty;
            var args = root.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array
                ? argsElement.EnumerateArray()
                    .Where(static e => e.ValueKind == JsonValueKind.String)
                    .Select(static e => e.GetString() ?? string.Empty)
                    .Where(static a => !string.IsNullOrWhiteSpace(a))
                    .ToArray()
                : [];

            return new CommandInvocation(command.Trim(), args);
        }
        catch (JsonException)
        {
            return ParseCommandLine(response);
        }
    }

    private static CommandInvocation ParseCommandLine(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return new CommandInvocation(string.Empty, []);

        var segments = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return new CommandInvocation(string.Empty, []);

        var command = segments[0];
        var args = segments.Skip(1).ToArray();
        return new CommandInvocation(command, args);
    }

    private async Task<string> ExecuteCommandAsync(CommandInvocation invocation, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = invocation.Command,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var arg in invocation.Args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"Failed to start command '{invocation.Command}': {ex.Message}";
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CommandTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return $"Command timed out after {CommandTimeout.TotalSeconds:0} seconds.";
            }

            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}";
        var trimmed = combined.Trim();
        if (trimmed.Length > MaxOutputLength)
            trimmed = $"{trimmed.Substring(0, MaxOutputLength)}\n...[output truncated]";

        return $"ExitCode: {process.ExitCode}\n{(string.IsNullOrWhiteSpace(trimmed) ? "(no output)" : trimmed)}";
    }

    private static IEnumerable<string> LoadAllowedCommandsFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("COMPASS_ALLOWED_COMMANDS");
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultAllowedCommands;

        var commands = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return commands.Length == 0 ? DefaultAllowedCommands : commands;
    }

    private sealed record CommandInvocation(string Command, string[] Args);
}
