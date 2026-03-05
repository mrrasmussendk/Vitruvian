using System.Diagnostics;
using VitruvianAbstractions.Interfaces;

namespace VitruvianAbstractions;

/// <summary>
/// Default <see cref="ICommandRunner"/> implementation backed by <see cref="Process"/>.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandExecutionResult> ExecuteAsync(
        string command,
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new CommandExecutionResult(
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                StartError: ex.Message);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return new CommandExecutionResult(
                    ExitCode: null,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    TimedOut: true);
            }

            throw;
        }

        return new CommandExecutionResult(
            ExitCode: process.ExitCode,
            StandardOutput: await stdoutTask,
            StandardError: await stderrTask);
    }
}
