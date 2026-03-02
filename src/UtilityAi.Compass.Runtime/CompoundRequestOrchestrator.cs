using System.Text.Json;
using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.Runtime;

/// <summary>
/// Orchestrates compound (multi-intent) request handling at the host level.
/// Detects compound requests via heuristics and decomposes them into independent
/// sub-tasks using the LLM. Each sub-task is then routed through the full
/// pipeline so any installed module can handle its domain automatically.
/// </summary>
/// <remarks>
/// This class is intentionally module-agnostic: it knows nothing about files,
/// SMS, weather, or any other capability. It only splits multi-intent requests
/// into independent sub-tasks and lets the standard orchestration pipeline
/// dispatch each one to the right module.
/// </remarks>
public static class CompoundRequestOrchestrator
{
    /// <summary>
    /// Heuristically detects whether a user request contains multiple independent intents
    /// (e.g., "create file u.txt then give me rainbow colors"). Uses the same compound
    /// indicators as <c>GoalRouterSensor.DetectMultiStepRequest</c>.
    /// </summary>
    /// <param name="text">The raw user request text.</param>
    /// <returns><see langword="true"/> when the text looks like a compound request.</returns>
    public static bool IsCompoundRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        // Sequential intent indicators
        var indicators = new[]
        {
            " then ", " and then ", " afterwards ", " after that ",
            " next ", " followed by ", " after "
        };
        if (indicators.Any(i => lower.Contains(i)))
            return true;

        // Multiple action verbs suggest multiple intents
        var verbs = new[]
        {
            "create", "write", "read", "delete", "update",
            "insert", "add", "remove", "modify", "input"
        };
        return verbs.Count(v => lower.Contains(v)) >= 2;
    }

    /// <summary>
    /// Uses the LLM to decompose a compound user request into independent, self-contained
    /// sub-tasks. Returns the original request as a single-element list when decomposition
    /// fails or is not needed.
    /// </summary>
    /// <param name="modelClient">The model client to use for decomposition; when <see langword="null"/>,
    /// the original input is returned as-is.</param>
    /// <param name="input">The compound user request text.</param>
    /// <param name="cancellationToken">Token to cancel the LLM call.</param>
    /// <returns>A list of independent sub-task strings.</returns>
    public static async Task<List<string>> DecomposeRequestAsync(
        IModelClient? modelClient,
        string input,
        CancellationToken cancellationToken)
    {
        if (modelClient is null)
            return [input];

        try
        {
            var response = await modelClient.GenerateAsync(
                new ModelRequest
                {
                    SystemMessage = "Split this compound user request into independent sub-tasks. " +
                                    "Return a JSON array of strings, one per task. " +
                                    "Keep each sub-task self-contained (include relevant context like filenames). " +
                                    "If not compound, return the original as a single-element array. " +
                                    "Only return valid JSON, nothing else.",
                    Prompt = input,
                    MaxTokens = 256
                },
                cancellationToken);

            using var doc = JsonDocument.Parse(response.Text);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var tasks = doc.RootElement.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToList();

                if (tasks.Count > 0)
                    return tasks;
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch
        {
            // LLM response wasn't valid JSON; fall through to single-request handling.
        }

        return [input];
    }

    /// <summary>
    /// Plans request execution using semantic decomposition when a model client is available.
    /// Falls back to the original single request when decomposition is unavailable or fails.
    /// </summary>
    public static Task<List<string>> PlanRequestsAsync(
        IModelClient? modelClient,
        string input,
        CancellationToken cancellationToken)
    {
        if (modelClient is null || !IsCompoundRequest(input))
            return Task.FromResult(new List<string> { input });

        return DecomposeRequestAsync(modelClient, input, cancellationToken);
    }
}
