using System.Collections.Concurrent;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Planning;
using UtilityAi.Compass.Abstractions;

namespace UtilityAi.Compass.Runtime.Planning;

/// <summary>
/// Executes a GOAP-style <see cref="ExecutionPlan"/> with support for:
/// <list type="bullet">
///   <item>Parallel execution of independent steps (multithreading)</item>
///   <item>Human-in-the-loop gating via <see cref="IApprovalGate"/></item>
///   <item>Per-step result caching</item>
///   <item>Plan outcome memory for future reference</item>
///   <item>Context window management (sliding window of recent results)</item>
/// </list>
/// </summary>
public sealed class PlanExecutor
{
    private readonly Dictionary<string, ICompassModule> _modules;
    private readonly IApprovalGate? _approvalGate;
    private readonly int _contextWindowSize;

    // In-memory cache: key = (domain + normalised input) → output
    private readonly ConcurrentDictionary<string, string> _cache = new();

    // In-memory durable store of completed plan results
    private readonly ConcurrentBag<PlanResult> _memory = new();

    /// <summary>Gets a snapshot of all remembered plan results.</summary>
    public IReadOnlyList<PlanResult> Memory => _memory.ToArray();

    /// <summary>
    /// Initialises a new <see cref="PlanExecutor"/>.
    /// </summary>
    /// <param name="modules">Map of domain → module instance.</param>
    /// <param name="approvalGate">Optional HITL gate; when provided, write/execute steps require approval.</param>
    /// <param name="contextWindowSize">Max number of prior step outputs injected as context into subsequent steps.</param>
    public PlanExecutor(
        Dictionary<string, ICompassModule> modules,
        IApprovalGate? approvalGate = null,
        int contextWindowSize = 4)
    {
        _modules = modules;
        _approvalGate = approvalGate;
        _contextWindowSize = contextWindowSize;
    }

    /// <summary>
    /// Executes the given plan, respecting dependency edges for parallelism,
    /// gating write/execute steps through HITL, and caching results.
    /// </summary>
    public async Task<PlanResult> ExecuteAsync(ExecutionPlan plan, string? userId, CancellationToken ct)
    {
        var results = new ConcurrentDictionary<string, PlanStepResult>();
        var stepOutputs = new ConcurrentDictionary<string, string>();

        // Group steps into waves: each wave contains steps whose dependencies are already satisfied.
        var remaining = new List<PlanStep>(plan.Steps);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(s => s.DependsOn.All(dep => results.ContainsKey(dep)))
                .ToList();

            if (ready.Count == 0)
            {
                // Circular dependency or missing dependency — execute remaining sequentially
                ready = [remaining[0]];
            }

            foreach (var step in ready)
                remaining.Remove(step);

            // Execute the ready wave in parallel
            var tasks = ready.Select(step => ExecuteStepAsync(step, userId, stepOutputs, ct));
            var waveResults = await Task.WhenAll(tasks);

            foreach (var result in waveResults)
            {
                results[result.StepId] = result;
                stepOutputs[result.StepId] = result.Output;
            }
        }

        // Aggregate
        var orderedResults = plan.Steps
            .Select(s => results.TryGetValue(s.StepId, out var r) ? r : new PlanStepResult(s.StepId, s.ModuleDomain, false, "Step not executed", DateTimeOffset.UtcNow, TimeSpan.Zero))
            .ToList();

        var aggregatedOutput = string.Join("\n\n", orderedResults.Select(r => r.Output));
        var planResult = new PlanResult(
            plan.PlanId,
            orderedResults.All(r => r.Success),
            orderedResults,
            aggregatedOutput);

        // Persist to memory
        _memory.Add(planResult);

        return planResult;
    }

    private async Task<PlanStepResult> ExecuteStepAsync(
        PlanStep step,
        string? userId,
        ConcurrentDictionary<string, string> priorOutputs,
        CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Cache check
        var cacheKey = $"{step.ModuleDomain}|{step.Input}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return new PlanStepResult(step.StepId, step.ModuleDomain, true, cached, started, sw.Elapsed);
        }

        // Resolve module
        if (string.IsNullOrEmpty(step.ModuleDomain) || !_modules.TryGetValue(step.ModuleDomain, out var module))
        {
            return new PlanStepResult(step.StepId, step.ModuleDomain, false,
                $"No module found for domain '{step.ModuleDomain}'", started, sw.Elapsed);
        }

        // HITL gating for write/execute operations
        if (_approvalGate is not null)
        {
            var opType = InferOperationType(step);
            if (opType is OperationType.Write or OperationType.Delete or OperationType.Execute)
            {
                var approved = await _approvalGate.ApproveAsync(opType, step.Description, step.ModuleDomain, ct);
                if (!approved)
                {
                    return new PlanStepResult(step.StepId, step.ModuleDomain, false,
                        $"Step denied by human reviewer: {step.Description}", started, sw.Elapsed);
                }
            }
        }

        // Build input with context window from prior steps
        var enrichedInput = BuildContextWindow(step.Input, priorOutputs);

        try
        {
            var output = await module.ExecuteAsync(enrichedInput, userId, ct);
            sw.Stop();

            // Cache the result
            _cache[cacheKey] = output;

            return new PlanStepResult(step.StepId, step.ModuleDomain, true, output, started, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new PlanStepResult(step.StepId, step.ModuleDomain, false,
                $"Error: {ex.Message}", started, sw.Elapsed);
        }
    }

    private string BuildContextWindow(string input, ConcurrentDictionary<string, string> priorOutputs)
    {
        if (priorOutputs.IsEmpty || _contextWindowSize <= 0)
            return input;

        var recentOutputs = priorOutputs.Values.TakeLast(_contextWindowSize).ToList();
        if (recentOutputs.Count == 0)
            return input;

        var context = string.Join("\n", recentOutputs.Select(o => Truncate(o, 200)));
        return $"[Prior step context:\n{context}\n]\n\n{input}";
    }

    private static OperationType InferOperationType(PlanStep step)
    {
        var lower = (step.Description + " " + step.Input).ToLowerInvariant();
        if (lower.Contains("delete") || lower.Contains("remove"))
            return OperationType.Delete;
        if (lower.Contains("execute") || lower.Contains("run") || lower.Contains("shell"))
            return OperationType.Execute;
        if (lower.Contains("write") || lower.Contains("create") || lower.Contains("update") || lower.Contains("modify") || lower.Contains("send"))
            return OperationType.Write;
        return OperationType.Read;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
