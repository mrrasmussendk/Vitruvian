using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Planning;
using UtilityAi.Compass.Runtime;
using UtilityAi.Compass.Runtime.Planning;
using UtilityAi.Compass.Runtime.Routing;
using UtilityAi.Compass.StandardModules;

namespace UtilityAi.Compass.Cli;

/// <summary>
/// GOAP-style request processor that creates a plan before executing.
/// Pipeline: Request → GoapPlanner → [HITL plan review] → PlanExecutor (parallel) → Memory + Cache → Response.
/// </summary>
internal sealed class RequestProcessor
{
    private readonly IHost _host;
    private readonly ModuleRouter _router;
    private readonly IModelClient? _modelClient;
    private readonly IApprovalGate? _approvalGate;
    private readonly Dictionary<string, ICompassModule> _modules = new();
    private readonly List<(string User, string Assistant)> _conversationHistory = new();
    private readonly GoapPlanner _planner;
    private PlanExecutor? _executor;

    private const int MaxConversationTurns = 10;

    public RequestProcessor(IHost host, ModuleRouter router, IModelClient? modelClient, IApprovalGate? approvalGate = null)
    {
        _host = host;
        _router = router;
        _modelClient = modelClient;
        _approvalGate = approvalGate;
        _planner = new GoapPlanner(modelClient);
    }

    /// <summary>Gets the current plan executor (created lazily after the first module is registered).</summary>
    internal PlanExecutor? Executor => _executor;

    public void RegisterModule(ICompassModule module)
    {
        _modules[module.Domain] = module;

        // Register with router (metadata is optional and defaults to 0.0 cost/risk)
        _router.RegisterModule(module, metadata: null);

        // Register with planner so it knows available capabilities
        _planner.RegisterModule(module.Domain, module.Description);
    }

    /// <summary>
    /// Gets a context-aware version of a module that wraps its model client with conversation history.
    /// </summary>
    private ICompassModule GetContextAwareModule(string domain)
    {
        if (!_modules.TryGetValue(domain, out var module))
            return null!;

        // If we don't have a model client, just return the original module
        if (_modelClient is null)
            return module;

        // Wrap the model client with context
        var contextAwareClient = new ContextAwareModelClient(_modelClient, _conversationHistory);

        // Create a new instance of the module with the context-aware client
        // This is a bit hacky but works without changing all modules
        return module switch
        {
            ConversationModule _ => new ConversationModule(contextAwareClient),
            WebSearchModule _ => new WebSearchModule(contextAwareClient),
            SummarizationModule _ => new SummarizationModule(contextAwareClient),
            GmailModule _ => new GmailModule(contextAwareClient),
            ShellCommandModule _ => new ShellCommandModule(contextAwareClient, GetDefaultWorkingDirectory()),
            FileOperationsModule fileModule => new FileOperationsModule(contextAwareClient, GetDefaultWorkingDirectory()),
            _ => module // Return original if we don't know how to wrap it
        };
    }

    public async Task<string> ProcessAsync(string input, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (_modules.Count == 0 && _modelClient is null)
        {
            return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";
        }

        // Phase 1: PLAN — create a GOAP-style plan before any execution
        var planStart = sw.ElapsedMilliseconds;
        var plan = await _planner.CreatePlanAsync(BuildEnrichedInput(input), cancellationToken);
        Console.WriteLine($"[GOAP] Plan created: {plan.PlanId} with {plan.Steps.Count} step(s)");
        foreach (var step in plan.Steps)
        {
            Console.WriteLine($"[GOAP]   {step.StepId}: {step.ModuleDomain} — {step.Description} (depends: [{string.Join(", ", step.DependsOn)}])");
        }
        Console.WriteLine($"[PERF] Planning: {sw.ElapsedMilliseconds - planStart}ms");

        // Phase 2: Handle steps with no module (fallback to direct LLM)
        if (plan.Steps.Count == 1 && string.IsNullOrEmpty(plan.Steps[0].ModuleDomain))
        {
            if (_modelClient is null)
                return "No model configured. Run 'compass --setup' or scripts/install.sh (Linux/macOS) / scripts/install.ps1 (Windows).";

            var fallbackResponse = await _modelClient.GenerateAsync(input, cancellationToken);
            await StoreConversationTurnAsync(input, fallbackResponse, cancellationToken);
            return fallbackResponse;
        }

        // Phase 3: EXECUTE — build context-aware module map and execute via PlanExecutor
        // Reuse the executor across requests to preserve cache and memory state
        if (_executor is null)
        {
            var contextAwareModules = new Dictionary<string, ICompassModule>();
            foreach (var kvp in _modules)
            {
                contextAwareModules[kvp.Key] = GetContextAwareModule(kvp.Key);
            }
            _executor = new PlanExecutor(contextAwareModules, _approvalGate);
        }

        var execStart = sw.ElapsedMilliseconds;
        var result = await _executor.ExecuteAsync(plan, null, cancellationToken);
        Console.WriteLine($"[PERF] Execution: {sw.ElapsedMilliseconds - execStart}ms (parallel steps supported)");

        // Phase 4: MEMORY — store conversation turn and log outcome
        var storeStart = sw.ElapsedMilliseconds;
        await StoreConversationTurnAsync(input, result.AggregatedOutput, cancellationToken);
        Console.WriteLine($"[PERF] StoreConversation: {sw.ElapsedMilliseconds - storeStart}ms");
        Console.WriteLine($"[GOAP] Plan {plan.PlanId} completed: success={result.Success}, memory_size={_executor.Memory.Count}");
        Console.WriteLine($"[PERF] Total: {sw.ElapsedMilliseconds}ms");

        return result.AggregatedOutput;
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

    private static string GetDefaultWorkingDirectory()
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "compass-workspace");
}
