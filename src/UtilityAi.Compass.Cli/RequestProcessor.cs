using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Runtime;
using UtilityAi.Compass.Runtime.Routing;

namespace UtilityAi.Compass.Cli;

/// <summary>
/// Simplified request processor using direct LLM-based module routing instead of UtilityAI orchestration.
/// </summary>
internal sealed class RequestProcessor
{
    private readonly IHost _host;
    private readonly ModuleRouter _router;
    private readonly IModelClient? _modelClient;
    private readonly Dictionary<string, ICompassModule> _modules = new();
    private readonly List<(string User, string Assistant)> _conversationHistory = new();

    private const int MaxConversationTurns = 10;

    public RequestProcessor(IHost host, ModuleRouter router, IModelClient? modelClient)
    {
        _host = host;
        _router = router;
        _modelClient = modelClient;
    }

    public void RegisterModule(ICompassModule module)
    {
        _modules[module.Domain] = module;

        // Register with router (metadata is optional and defaults to 0.0 cost/risk)
        _router.RegisterModule(module, metadata: null);
    }

    public async Task<string> ProcessAsync(string input, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Check if this is a compound request
        var planStart = sw.ElapsedMilliseconds;
        var requests = await CompoundRequestOrchestrator.PlanRequestsAsync(_modelClient, input, cancellationToken);
        Console.WriteLine($"[PERF] PlanRequests: {sw.ElapsedMilliseconds - planStart}ms");

        string responseText;
        if (requests.Count > 1)
        {
            var allResponses = new List<string>();
            foreach (var request in requests)
            {
                var response = await ExecuteSingleRequestAsync(request, null, cancellationToken);
                allResponses.Add(response);
            }
            responseText = string.Join("\n\n", allResponses);
        }
        else
        {
            var execStart = sw.ElapsedMilliseconds;
            responseText = await ExecuteSingleRequestAsync(input, null, cancellationToken);
            Console.WriteLine($"[PERF] ExecuteSingleRequest: {sw.ElapsedMilliseconds - execStart}ms");
        }

        var storeStart = sw.ElapsedMilliseconds;
        await StoreConversationTurnAsync(input, responseText, cancellationToken);
        Console.WriteLine($"[PERF] StoreConversation: {sw.ElapsedMilliseconds - storeStart}ms");
        Console.WriteLine($"[PERF] Total: {sw.ElapsedMilliseconds}ms");

        return responseText;
    }

    private async Task<string> ExecuteSingleRequestAsync(string input, string? userId, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Build enriched input with conversation context for routing
        var enrichedInput = BuildEnrichedInput(input);

        // Route to the best module
        var routeStart = sw.ElapsedMilliseconds;
        var selectedDomain = await _router.SelectModuleAsync(enrichedInput, cancellationToken);
        Console.WriteLine($"[PERF]   Router.SelectModule: {sw.ElapsedMilliseconds - routeStart}ms (selected: {selectedDomain ?? "none"})");

        if (selectedDomain is null || !_modules.TryGetValue(selectedDomain, out var module))
        {
            if (_modelClient is null)
            {
                return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";
            }

            // Fallback to direct LLM response
            var llmStart = sw.ElapsedMilliseconds;
            var fallbackResponse = await _modelClient.GenerateAsync(input, cancellationToken);
            Console.WriteLine($"[PERF]   Fallback LLM: {sw.ElapsedMilliseconds - llmStart}ms");
            return fallbackResponse;
        }

        // Execute the selected module with enriched input that includes context
        var execStart = sw.ElapsedMilliseconds;
        try
        {
            var response = await module.ExecuteAsync(enrichedInput, userId, cancellationToken);
            Console.WriteLine($"[PERF]   Module.Execute ({selectedDomain}): {sw.ElapsedMilliseconds - execStart}ms");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Module {selectedDomain} failed: {ex.Message}");
            return $"Error executing {selectedDomain}: {ex.Message}";
        }
    }

    private Task StoreConversationTurnAsync(string input, string responseText, CancellationToken cancellationToken)
    {
        // Store in-memory conversation history
        _conversationHistory.Add((input, responseText));

        // Keep only recent turns
        while (_conversationHistory.Count > MaxConversationTurns)
        {
            _conversationHistory.RemoveAt(0);
        }

        return Task.CompletedTask;
    }

    private string BuildEnrichedInput(string input)
    {
        // If there's recent conversation history, provide context to improve routing and execution
        if (_conversationHistory.Count == 0)
            return input;

        // Get the most recent exchange(s) - up to 2 turns for better context
        var recentTurns = _conversationHistory.TakeLast(Math.Min(2, _conversationHistory.Count)).ToList();

        if (recentTurns.Count == 0)
            return input;

        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("[Recent conversation context:");

        foreach (var (user, assistant) in recentTurns)
        {
            contextBuilder.AppendLine($"  User: {TruncateForContext(user, 100)}");
            contextBuilder.AppendLine($"  Assistant: {TruncateForContext(assistant, 150)}");
        }

        contextBuilder.AppendLine($"]\n\nCurrent user message: {input}");

        return contextBuilder.ToString();
    }

    private static string TruncateForContext(string text, int maxLength = 200)
    {
        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength) + "...";
    }
}
