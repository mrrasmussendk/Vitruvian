namespace VitruvianCli;

/// <summary>
/// Shared console input helpers used by CLI commands and the interactive REPL.
/// </summary>
public static class ConsoleHelper
{
    /// <summary>
    /// Reads a line of secret input from the console, masking each character with <c>*</c>.
    /// Falls back to <see cref="Console.ReadLine"/> when input is redirected (e.g. piped input).
    /// </summary>
    public static string ReadSecretFromConsole()
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

    /// <summary>
    /// Prints the list of available interactive REPL commands.
    /// </summary>
    public static void PrintCommands()
    {
        Console.WriteLine("Commands: /help, /setup, /list-modules, /configure-modules, /install-module <path|package@version> [--allow-unsigned], /load-module <path-to-dll>, /unregister-module <domain|filename>, /inspect-module <path|package@version> [--json], /doctor [--json], /policy validate <policyFile>, /policy explain <request>, /audit list, /audit show <id> [--json], /replay <id> [--no-exec], /new-module <Name> [OutputPath], /schedule \"<interval>\" <request>, /list-tasks, /cancel-task <id>, quit");
    }
}
