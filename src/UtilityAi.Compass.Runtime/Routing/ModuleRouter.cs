using System.Text.Json;
using System.Text.Json.Serialization;
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
        {
            Console.WriteLine("[ROUTER] No model client available, using fallback keyword matching");
            return FallbackSelection(request);
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = $"User request: {request}\n\nSelect the most appropriate module domain to handle this request. Return ONLY valid JSON with no extra text: {{\"domain\":\"<domain>\",\"confidence\":0-1,\"reason\":\"<reason>\"}}";

        var response = await _modelClient.CompleteAsync(
            systemMessage: systemPrompt,
            userMessage: userPrompt,
            cancellationToken: ct);

        try
        {
            // Clean up response - remove markdown code blocks if present
            var jsonText = response.Trim();
            if (jsonText.StartsWith("```"))
            {
                var lines = jsonText.Split('\n');
                jsonText = string.Join('\n', lines.Skip(1).Take(lines.Length - 2));
            }
            if (jsonText.StartsWith("```json"))
            {
                jsonText = jsonText.Substring(7);
                var endIndex = jsonText.IndexOf("```");
                if (endIndex > 0)
                    jsonText = jsonText.Substring(0, endIndex);
            }

            Console.WriteLine($"[ROUTER] Attempting to parse JSON: {jsonText.Trim()}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var selection = JsonSerializer.Deserialize<ModuleSelection>(jsonText.Trim(), options);
            if (selection is not null)
            {
                // Check if deserialization produced valid data
                if (string.IsNullOrWhiteSpace(selection.Domain))
                {
                    Console.WriteLine($"[ROUTER] WARNING: Deserialized selection has empty domain");
                    Console.WriteLine($"[ROUTER] Raw JSON was: {jsonText.Trim()}");
                    throw new InvalidOperationException("Empty domain in selection");
                }

                Console.WriteLine($"[ROUTER] LLM selected: {selection.Domain} (confidence: {selection.Confidence:F2})");
                Console.WriteLine($"[ROUTER] Reason: {selection.Reason}");

                // Require higher confidence for non-conversation modules to avoid mis-routing
                var requiredConfidence = selection.Domain == "conversation" ? 0.3 : 0.6;

                if (selection.Confidence >= requiredConfidence)
                {
                    // Validate the domain actually exists
                    if (_modules.Any(m => m.Domain == selection.Domain))
                        return selection.Domain;

                    Console.WriteLine($"[ROUTER] WARNING: Selected domain '{selection.Domain}' not found in registered modules");
                }
                else
                {
                    Console.WriteLine($"[ROUTER] Confidence too low ({selection.Confidence:F2} < {requiredConfidence:F2}), using fallback");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ROUTER] JSON parsing failed: {ex.Message}");
            Console.WriteLine($"[ROUTER] Raw response: {response}");
            // Fall back to simple matching if JSON parsing fails
        }

        Console.WriteLine("[ROUTER] Falling back to keyword matching");
        return FallbackSelection(request);
    }

    private string BuildSystemPrompt()
    {
        var moduleList = string.Join("\n", _modules.Select(m =>
            $"- {m.Domain}: {m.Description}"));

        return @"You are a precise module router. Select the BEST module to handle each user request.

Available modules:
" + moduleList + @"

CRITICAL ROUTING RULES:
1. file-operations: ONLY for requests with EXPLICIT filenames (e.g., ""read notes.txt"", ""create file.json"")
   - DO NOT use for: weather, search, general questions, single-word inputs
2. web-search: For weather, news, current events, real-time data, lookups
3. conversation: Fallback for general questions, explanations, when no specialized module fits

Examples:
- ""read notes.txt"" → file-operations (explicit filename)
- ""copenhagen"" → conversation (no filename, likely follow-up)
- ""weather tomorrow"" → web-search (current data needed)
- ""what is 2+2"" → conversation (general question)
- ""create hello.txt"" → file-operations (explicit file creation)

Confidence scoring:
- 0.9+: Perfect match (e.g., ""read file.txt"" → file-operations)
- 0.7-0.9: Strong match with clear intent
- 0.5-0.7: Reasonable match but some ambiguity
- <0.5: Uncertain, should use conversation fallback

Return ONLY valid JSON, no extra text: {""domain"":""<domain>"",""confidence"":0-1,""reason"":""<brief explanation>""}";
    }

    private string? FallbackSelection(string request)
    {
        // Simple keyword matching as fallback when LLM is unavailable
        // Score each module based on keyword overlap with request and description
        var lowerRequest = request.ToLowerInvariant();
        var requestWords = lowerRequest.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        var scoredModules = _modules
            .Select(m => new
            {
                Module = m,
                Score = CalculateMatchScore(requestWords, m.Description.ToLowerInvariant())
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Module.Cost) // Prefer lower cost when scores are equal
            .ToList();

        Console.WriteLine("[ROUTER] Fallback keyword scoring:");
        foreach (var scored in scoredModules.Take(3))
        {
            Console.WriteLine($"  - {scored.Module.Domain}: score={scored.Score}");
        }

        var bestMatch = scoredModules.FirstOrDefault();

        // Return best match if score is reasonable, otherwise default to conversation
        if (bestMatch != null && bestMatch.Score > 0)
        {
            Console.WriteLine($"[ROUTER] Fallback selected: {bestMatch.Module.Domain}");
            return bestMatch.Module.Domain;
        }

        // If no matches, try to find a conversation/general fallback module
        var conversationModule = _modules.FirstOrDefault(m => m.Description.Contains("conversation", StringComparison.OrdinalIgnoreCase)
                                            || m.Description.Contains("general", StringComparison.OrdinalIgnoreCase)
                                            || m.Description.Contains("answer", StringComparison.OrdinalIgnoreCase))?.Domain
               ?? _modules.FirstOrDefault()?.Domain; // Ultimate fallback: first available module

        Console.WriteLine($"[ROUTER] Fallback selected: {conversationModule} (no keyword matches)");
        return conversationModule;
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
