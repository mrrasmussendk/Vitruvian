using System.Text.Json;
using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.Runtime.Routing;

/// <summary>
/// Routes user requests to the most appropriate module using LLM-based selection.
/// </summary>
public sealed class ModuleRouter
{
    private readonly IModelClient? _modelClient;
    private readonly List<ModuleDescriptor> _modules = new();

    public ModuleRouter(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    /// <summary>
    /// Registers a module for routing consideration.
    /// </summary>
    public void RegisterModule(ICompassModule module, ProposalMetadata? metadata = null)
    {
        _modules.Add(new ModuleDescriptor(
            module.Domain,
            module.Description,
            metadata?.EstimatedCost ?? 0.0,
            metadata?.RiskLevel ?? 0.0));
    }

    /// <summary>
    /// Selects the most appropriate module for the given request using LLM reasoning.
    /// Falls back to simple matching if no model client is available.
    /// </summary>
    public async Task<string?> SelectModuleAsync(string request, CancellationToken ct)
    {
        if (_modules.Count == 0)
            return null;

        if (_modelClient is null)
            return FallbackSelection(request);

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = $"User request: {request}\n\nSelect the most appropriate module domain to handle this request. Return JSON: {{\"domain\":\"<domain>\",\"confidence\":0-1,\"reason\":\"<reason>\"}}";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemPrompt,
            userMessage: userPrompt,
            cancellationToken: ct);

        try
        {
            var selection = JsonSerializer.Deserialize<ModuleSelection>(response);
            if (selection is not null && selection.Confidence > 0.5)
                return selection.Domain;
        }
        catch
        {
            // Fall back to simple matching if JSON parsing fails
        }

        return FallbackSelection(request);
    }

    private string BuildSystemPrompt()
    {
        var moduleList = string.Join("\n", _modules.Select(m =>
            $"- {m.Domain}: {m.Description} (cost={m.Cost:F2}, risk={m.Risk:F2})"));

        return @"You are a module router for a capability dispatch system. Select the best module to handle each user request.

Available modules:
" + moduleList + @"

Selection guidelines:
1. Match the user's intent to the module description
2. Consider cost and risk - prefer lower values when multiple modules could work
3. Return high confidence (>0.8) for clear matches
4. Return lower confidence (0.5-0.8) when uncertain
5. The ""conversation"" module is a general fallback for Q&A

Return JSON format: {""domain"":""<domain>"",""confidence"":0-1,""reason"":""<brief explanation>""}";
    }

    private string? FallbackSelection(string request)
    {
        // Simple keyword matching as fallback when LLM is unavailable
        // Score each module based on keyword overlap with request and description
        var lowerRequest = request.ToLowerInvariant();
        var requestWords = lowerRequest.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        var bestMatch = _modules
            .Select(m => new
            {
                Module = m,
                Score = CalculateMatchScore(requestWords, m.Description.ToLowerInvariant())
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Module.Cost) // Prefer lower cost when scores are equal
            .FirstOrDefault();

        // Return best match if score is reasonable, otherwise default to conversation
        if (bestMatch != null && bestMatch.Score > 0)
            return bestMatch.Module.Domain;

        // If no matches, try to find a conversation/general fallback module
        return _modules.FirstOrDefault(m => m.Description.Contains("conversation", StringComparison.OrdinalIgnoreCase)
                                            || m.Description.Contains("general", StringComparison.OrdinalIgnoreCase)
                                            || m.Description.Contains("answer", StringComparison.OrdinalIgnoreCase))?.Domain
               ?? _modules.FirstOrDefault()?.Domain; // Ultimate fallback: first available module
    }

    private static int CalculateMatchScore(string[] requestWords, string description)
    {
        var descriptionWords = description.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var score = 0;

        foreach (var requestWord in requestWords)
        {
            if (requestWord.Length < 3) continue; // Skip short words like "of", "a", "to"

            foreach (var descWord in descriptionWords)
            {
                if (descWord.Contains(requestWord) || requestWord.Contains(descWord))
                {
                    score += 1;
                }
            }
        }

        return score;
    }

    private sealed record ModuleDescriptor(string Domain, string Description, double Cost, double Risk);
    private sealed record ModuleSelection(string Domain, double Confidence, string Reason);
}
