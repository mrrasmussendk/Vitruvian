using System.Text.Json;
using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianPluginSdk.Attributes;

namespace VitruvianStandardModules;

/// <summary>
/// Shell command module implementing IVitruvianModule.
/// Executes a guarded allowlist of commands in the configured working directory.
/// </summary>
[RequiresPermission(ModuleAccess.Execute)]
[RequiresPermission(ModuleAccess.Read)]
public sealed class ShellCommandModule : IVitruvianModule
{
    private static readonly string CommandParsingPrompt = OperatingSystem.IsWindows()
        ? """
Determine which command should be executed from the user request.
Return ONLY valid JSON in this format: {"command":"command-name","args":["arg1","arg2"]}
- command: executable name only, no shell wrappers
- args: argument array (empty array if no args)

IMPORTANT: This system is running Windows. Use Windows-native commands:
- Current directory: {"command":"cmd","args":["/c","cd"]}
- List files: {"command":"cmd","args":["/c","dir"]}
- List files in path: {"command":"cmd","args":["/c","dir","C:\\path"]}
- Display file contents: {"command":"cmd","args":["/c","type","filename.txt"]}
- Echo/print text: {"command":"cmd","args":["/c","echo","some text"]}
- System version: {"command":"cmd","args":["/c","ver"]}
- Current user: {"command":"whoami","args":[]}
- Git commands: {"command":"git","args":["status"]} (git is cross-platform)
- Dotnet commands: {"command":"dotnet","args":["--version"]} (dotnet is cross-platform)

DO NOT use Unix commands like pwd, ls, cat - they don't exist on Windows.
"""
        : """
Determine which command should be executed from the user request.
Return ONLY valid JSON in this format: {"command":"command-name","args":["arg1","arg2"]}
- command: executable name only, no shell wrappers
- args: argument array (empty array if no args)

IMPORTANT: This system is running Unix/Linux/macOS. Use Unix-native commands:
- Current directory: {"command":"pwd","args":[]}
- List files: {"command":"ls","args":[]}
- List files in path: {"command":"ls","args":["/path"]}
- Display file contents: {"command":"cat","args":["filename.txt"]}
- Echo/print text: {"command":"echo","args":["some text"]}
- System info: {"command":"uname","args":["-a"]}
- Current user: {"command":"whoami","args":[]}
- Git commands: {"command":"git","args":["status"]}
- Dotnet commands: {"command":"dotnet","args":["--version"]}
""";
    private const int MaxOutputLength = 8_000;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] DefaultAllowedCommands = OperatingSystem.IsWindows()
        ? [
            "cmd",
            "date",
            "dir",
            "dotnet",
            "echo",
            "git",
            "powershell",
            "type",
            "where",
            "whoami"
        ]
        : [
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
    private readonly ICommandRunner _commandRunner;
    private readonly string _workingDirectory;
    private readonly HashSet<string> _allowedCommands;

    public string Domain => "shell-command";
    public string Description => OperatingSystem.IsWindows()
        ? "Execute approved Windows command-line operations (cmd, dir, type, git, dotnet, etc.)"
        : "Execute approved shell/command-line operations (ls, pwd, cat, git, dotnet, etc.)";

    public ShellCommandModule(
        IModelClient? modelClient = null,
        string? workingDirectory = null,
        IEnumerable<string>? allowedCommands = null,
        ICommandRunner? commandRunner = null)
    {
        _modelClient = modelClient;
        _commandRunner = commandRunner ?? new ProcessCommandRunner();
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
            return TranslateForPlatform(ParseCommandLine(request));

        try
        {
            var response = await _modelClient.CompleteAsync(
                systemMessage: CommandParsingPrompt,
                userMessage: $"Analyze this shell/command request: {request}",
                cancellationToken: ct);

            return TranslateForPlatform(ParseCommandResponse(response));
        }
        catch
        {
            return TranslateForPlatform(ParseCommandLine(request));
        }
    }

    private static CommandInvocation TranslateForPlatform(CommandInvocation invocation)
    {
        if (!OperatingSystem.IsWindows())
            return invocation;

        // Translate common Unix commands to Windows equivalents
        return invocation.Command.ToLowerInvariant() switch
        {
            "pwd" => new CommandInvocation("cmd", ["/c", "cd"]),
            "ls" when invocation.Args.Length == 0 => new CommandInvocation("cmd", ["/c", "dir"]),
            "ls" => new CommandInvocation("cmd", ["/c", "dir", .. invocation.Args]),
            "cat" when invocation.Args.Length > 0 => new CommandInvocation("cmd", ["/c", "type", .. invocation.Args]),
            "echo" => new CommandInvocation("cmd", ["/c", "echo", .. invocation.Args]),
            "uname" => new CommandInvocation("cmd", ["/c", "ver"]),
            _ => invocation
        };
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
        var result = await _commandRunner.ExecuteAsync(
            invocation.Command,
            invocation.Args,
            _workingDirectory,
            CommandTimeout,
            ct);

        if (!string.IsNullOrWhiteSpace(result.StartError))
            return $"Failed to start command '{invocation.Command}': {result.StartError}";

        if (result.TimedOut)
            return $"Command timed out after {CommandTimeout.TotalSeconds:0} seconds.";

        var combined = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : $"{result.StandardOutput}\n{result.StandardError}";
        var trimmed = combined.Trim();
        if (trimmed.Length > MaxOutputLength)
            trimmed = $"{trimmed[..MaxOutputLength]}\n...[output truncated]";

        return $"ExitCode: {result.ExitCode ?? -1}\n{(string.IsNullOrWhiteSpace(trimmed) ? "(no output)" : trimmed)}";
    }

    private static IEnumerable<string> LoadAllowedCommandsFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("VITRUVIAN_ALLOWED_COMMANDS");
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
