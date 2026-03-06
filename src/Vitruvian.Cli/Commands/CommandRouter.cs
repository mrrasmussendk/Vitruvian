namespace VitruvianCli.Commands;

/// <summary>
/// Routes CLI startup arguments to the appropriate <see cref="ICliCommand"/> handler.
/// Commands are evaluated in registration order; the first matching command wins.
/// </summary>
public sealed class CommandRouter
{
    private readonly IReadOnlyList<ICliCommand> _commands;

    public CommandRouter(IReadOnlyList<ICliCommand> commands)
    {
        _commands = commands;
    }

    /// <summary>
    /// Attempts to find a command that can handle the given startup arguments.
    /// </summary>
    public bool TryRoute(string[] args, out ICliCommand command)
    {
        foreach (var candidate in _commands)
        {
            if (candidate.CanHandle(args))
            {
                command = candidate;
                return true;
            }
        }

        command = default!;
        return false;
    }

    /// <summary>
    /// Creates a <see cref="CommandRouter"/> with all built-in CLI commands.
    /// </summary>
    public static CommandRouter CreateDefault(string pluginsPath, string modulesPath)
    {
        var commands = new List<ICliCommand>
        {
            new HelpCommand(),
            new ListModulesCommand(pluginsPath, modulesPath),
            new InstallModuleCommand(pluginsPath),
            new InspectModuleCommand(),
            new DoctorCommand(pluginsPath),
            new PolicyValidateCommand(),
            new PolicyExplainCommand(),
            new AuditListCommand(),
            new AuditShowCommand(),
            new ReplayCommand(),
            new NewModuleCommand(),
            new SetupCommand(),
            new ConfigureModulesCommand(modulesPath),
            new ModelCommand()
        };

        return new CommandRouter(commands);
    }
}
