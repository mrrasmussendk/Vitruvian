using System.Text.Json;

namespace VitruvianCli.Commands;

/// <summary>
/// Inspects a module package and displays metadata, capabilities, and security findings.
/// </summary>
public sealed class InspectModuleCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 2 &&
        (string.Equals(args[0], "inspect-module", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--inspect-module", StringComparison.OrdinalIgnoreCase));

    public async Task<int> ExecuteAsync(string[] args)
    {
        var report = await ModuleInstaller.InspectAsync(args[1]);
        var asJson = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
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

        return 0;
    }
}
