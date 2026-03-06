namespace VitruvianCli.Commands;

/// <summary>
/// Configures the model provider and optional model name.
/// </summary>
public sealed class ModelCommand : ICliCommand
{
    private static readonly string[] SupportedProviders = ["openai", "anthropic", "gemini"];

    public bool CanHandle(string[] args) =>
        args.Length >= 2 &&
        (string.Equals(args[0], "--model", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "/model", StringComparison.OrdinalIgnoreCase));

    public Task<int> ExecuteAsync(string[] args)
    {
        var modelArg = args[1];
        var colonIndex = modelArg.IndexOf(':');
        var provider = colonIndex >= 0 ? modelArg[..colonIndex] : modelArg;
        var modelName = colonIndex >= 0 ? modelArg[(colonIndex + 1)..] : null;

        if (!SupportedProviders.Any(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"Unknown provider '{provider}'. Supported: {string.Join(", ", SupportedProviders)}.");
            return Task.FromResult(1);
        }

        EnvFileLoader.PersistSecret("VITRUVIAN_MODEL_PROVIDER", provider.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(modelName))
            EnvFileLoader.PersistSecret("VITRUVIAN_MODEL_NAME", modelName);
        EnvFileLoader.Load(overwriteExisting: true);
        Console.WriteLine($"Model provider set to '{provider}'" +
            (string.IsNullOrWhiteSpace(modelName) ? " (using default model)." : $" with model '{modelName}'."));
        return Task.FromResult(0);
    }
}
