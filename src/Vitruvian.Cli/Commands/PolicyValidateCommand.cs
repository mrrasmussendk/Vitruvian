using System.Text.Json;

namespace VitruvianCli.Commands;

/// <summary>
/// Validates a policy file for correct structure.
/// </summary>
public sealed class PolicyValidateCommand : ICliCommand
{
    public bool CanHandle(string[] args) =>
        args.Length >= 3 &&
        (string.Equals(args[0], "policy", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--policy", StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(args[1], "validate", StringComparison.OrdinalIgnoreCase);

    public async Task<int> ExecuteAsync(string[] args)
    {
        var policyPath = args[2];
        if (!File.Exists(policyPath))
        {
            Console.WriteLine($"Policy validation failed: '{policyPath}' not found.");
            return 1;
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
            return hasRules ? 0 : 1;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Policy validation failed: {ex.Message}");
            return 1;
        }
    }
}
