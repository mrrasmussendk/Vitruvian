using System.Text.Json;

namespace VitruvianCli.Commands;

/// <summary>
/// Runs a health check on the Vitruvian installation and reports findings.
/// </summary>
public sealed class DoctorCommand : ICliCommand
{
    private readonly string _pluginsPath;

    public DoctorCommand(string pluginsPath)
    {
        _pluginsPath = pluginsPath;
    }

    public bool CanHandle(string[] args) =>
        args.Length >= 1 &&
        (string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--doctor", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        var hasUnsigned = ModuleInstaller.ListInstalledModules(_pluginsPath).Any();
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

        if (args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Doctor status: {report.Status}");
            foreach (var finding in report.Findings)
                Console.WriteLine($"  - {finding}");
        }

        return Task.FromResult(0);
    }
}
