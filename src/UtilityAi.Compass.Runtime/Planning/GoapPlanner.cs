using System.Text.Json;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Planning;

namespace UtilityAi.Compass.Runtime.Planning;

/// <summary>
/// GOAP-style planner that produces an <see cref="ExecutionPlan"/> from a user request
/// and the set of available modules. The plan is generated <em>before</em> any execution
/// begins, allowing HITL review, caching, and parallel scheduling.
/// </summary>
public sealed class GoapPlanner
{
    private readonly IModelClient? _modelClient;
    private readonly List<ModuleInfo> _modules = [];

    public GoapPlanner(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    /// <summary>Registers a module so the planner knows it is available.</summary>
    public void RegisterModule(string domain, string description)
    {
        _modules.Add(new ModuleInfo(domain, description));
    }

    /// <summary>
    /// Creates a GOAP-style execution plan for the given user request.
    /// When no model client is available, falls back to a single-step plan
    /// using keyword-based module matching.
    /// </summary>
    public async Task<ExecutionPlan> CreatePlanAsync(string request, CancellationToken ct)
    {
        var planId = Guid.NewGuid().ToString("N")[..12];

        if (_modules.Count == 0)
            return SingleStepPlan(planId, request, domain: null);

        if (_modelClient is null)
            return SingleStepPlan(planId, request, FallbackDomain(request));

        try
        {
            var systemPrompt = BuildPlannerPrompt();
            var userPrompt = $"User request: {request}\n\nProduce a plan as a JSON array. Each element: {{\"step_id\":\"s1\",\"module\":\"<domain>\",\"description\":\"<what>\",\"input\":\"<request text>\",\"depends_on\":[]}}. Independent steps should have empty depends_on so they can run in parallel. Return ONLY valid JSON.";

            var response = await _modelClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken: ct);
            var plan = ParsePlanResponse(planId, request, response);
            if (plan is not null)
                return plan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall through to single-step fallback.
        }

        return SingleStepPlan(planId, request, FallbackDomain(request));
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private string BuildPlannerPrompt()
    {
        var moduleList = string.Join("\n", _modules.Select(m => $"- {m.Domain}: {m.Description}"));

        return $"""
            You are a GOAP (Goal-Oriented Action Planning) planner.
            Given a user request and the available modules below, produce an execution plan
            as an ordered list of steps.  Each step targets exactly one module.

            Available modules:
            {moduleList}

            Rules:
            1. If the request can be satisfied by a single module, return a single-step plan.
            2. If the request requires multiple modules, break it into the minimal set of steps.
            3. Mark steps that do NOT depend on earlier output with an empty "depends_on" array
               so they can execute in parallel.
            4. Steps that need output from a prior step must list its step_id in "depends_on".
            5. Always include a brief "description" of what the step accomplishes.
            6. The "input" field should be a self-contained request the module can execute.

            Return ONLY a JSON array, no markdown fences or extra text.
            """;
    }

    private ExecutionPlan? ParsePlanResponse(string planId, string originalRequest, string response)
    {
        var json = CleanJson(response);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var steps = new List<PlanStep>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var stepId = el.TryGetProperty("step_id", out var sid) ? sid.GetString() ?? $"s{steps.Count + 1}" : $"s{steps.Count + 1}";
            var module = el.TryGetProperty("module", out var mod) ? mod.GetString() ?? "" : "";
            var desc = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var input = el.TryGetProperty("input", out var inp) ? inp.GetString() ?? originalRequest : originalRequest;
            var deps = new List<string>();
            if (el.TryGetProperty("depends_on", out var depArr) && depArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in depArr.EnumerateArray())
                {
                    var depStr = dep.GetString();
                    if (!string.IsNullOrEmpty(depStr))
                        deps.Add(depStr);
                }
            }

            // Validate that the module exists; skip unknown modules.
            if (!_modules.Any(m => m.Domain == module))
                continue;

            steps.Add(new PlanStep(stepId, module, desc, input, deps));
        }

        return steps.Count > 0
            ? new ExecutionPlan(planId, originalRequest, steps)
            : null;
    }

    private ExecutionPlan SingleStepPlan(string planId, string request, string? domain)
    {
        var step = new PlanStep(
            StepId: "s1",
            ModuleDomain: domain ?? "",
            Description: "Execute request",
            Input: request,
            DependsOn: []);

        return new ExecutionPlan(planId, request, [step]);
    }

    private string? FallbackDomain(string request)
    {
        var lower = request.ToLowerInvariant();
        var words = lower.Split([' ', '.', ',', '!', '?'], StringSplitOptions.RemoveEmptyEntries);

        var best = _modules
            .Select(m => new { m.Domain, Score = ScoreModule(words, m.Description.ToLowerInvariant()) })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        return best?.Score > 0 ? best.Domain : _modules.FirstOrDefault()?.Domain;
    }

    private static int ScoreModule(string[] words, string description)
    {
        var descWords = description.Split([' ', '.', ','], StringSplitOptions.RemoveEmptyEntries);
        return words.Where(w => w.Length >= 3).Sum(w => descWords.Count(dw => dw.Contains(w) || w.Contains(dw)));
    }

    private static string CleanJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
            var end = trimmed.IndexOf("```", StringComparison.Ordinal);
            if (end > 0) trimmed = trimmed[..end];
        }
        else if (trimmed.StartsWith("```"))
        {
            var lines = trimmed.Split('\n');
            trimmed = string.Join('\n', lines.Skip(1).Take(lines.Length - 2));
        }
        return trimmed.Trim();
    }

    private sealed record ModuleInfo(string Domain, string Description);
}
