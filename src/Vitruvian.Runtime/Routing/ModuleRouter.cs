using System.Text.Json;
using System.Text.Json.Serialization;
using VitruvianAbstractions.Facts;
using VitruvianAbstractions.Interfaces;
using VitruvianRuntime.DI;

namespace VitruvianRuntime.Routing;

/// <summary>
/// Routes user requests to the most appropriate module using LLM-based selection.
/// </summary>
public sealed class ModuleRouter
{
    private readonly IModelClient? _modelClient;
    private readonly List<ModuleDescriptor> _modules = new();
    private readonly RouterOptions _options;

    public ModuleRouter(IModelClient? modelClient = null, RouterOptions? options = null)
    {
        _modelClient = modelClient;
        _options = options ?? new RouterOptions();
    }

    /// <summary>
    /// Registers a module for routing consideration.
    /// </summary>
    public void RegisterModule(IVitruvianModule module, ProposalMetadata? metadata = null)
    {
        _modules.Add(new ModuleDescriptor(
            module.Domain,
            module.Description,
            metadata?.EstimatedCost ?? 0.0,
            metadata?.RiskLevel ?? 0.0));
    }

    /// <summary>
    /// Unregisters a module by its domain name so it is no longer considered for routing.
    /// </summary>
    /// <param name="domain">The domain identifier of the module to remove.</param>
    /// <returns><c>true</c> if a module was found and removed; otherwise <c>false</c>.</returns>
    public bool UnregisterModule(string domain)
    {
        return _modules.RemoveAll(m => m.Domain == domain) > 0;
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
            var jsonText = JsonUtilities.CleanMarkdownCodeFences(response);

            Console.WriteLine($"[ROUTER] Attempting to parse JSON: {jsonText}");

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
                var requiredConfidence = selection.Domain == "conversation"
                    ? _options.ConversationConfidenceThreshold
                    : _options.SpecializedConfidenceThreshold;

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
2. shell-command: For command-line/shell execution requests (e.g., ""run dotnet --version"", ""show git status"")
3. web-search: For weather, news, current events, real-time data, lookups
4. conversation: Fallback for general questions, explanations, when no specialized module fits

Examples:
- ""read notes.txt"" → file-operations (explicit filename)
- ""copenhagen"" → conversation (no filename, likely follow-up)
- ""weather tomorrow"" → web-search (current data needed)
- ""what is 2+2"" → conversation (general question)
- ""create hello.txt"" → file-operations (explicit file creation)
- ""run dotnet test"" → shell-command (explicit command request)

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
        var scoredModules = _modules
            .Select(m => new
            {
                Module = m,
                Score = TextMatchingUtilities.CalculateMatchScore(request, m.Description)
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

    private sealed record ModuleDescriptor(string Domain, string Description, double Cost, double Risk);
    private sealed record ModuleSelection(string Domain, double Confidence, string Reason);
}
