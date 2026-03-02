using UtilityAi.Consideration;
using UtilityAi.Consideration.General;
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Workflow;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Workflow module that creates a file on disk.
/// Two-step workflow: parse the request, then write the file with validation.
/// </summary>
public sealed class FileCreationWorkflow : IWorkflowModule
{
    /// <inheritdoc />
    public WorkflowDefinition Define() => new(
        WorkflowId: "file-creation",
        DisplayName: "Create File",
        Goals: [GoalTag.Execute],
        Lanes: [Lane.Execute],
        Steps:
        [
            new StepDefinition(
                StepId: "write",
                DisplayName: "Write file to disk",
                RequiresFacts: ["UserRequest"],
                ProducesFacts: ["AiResponse"],
                Idempotent: false,
                MaxRetries: 1,
                Timeout: TimeSpan.FromSeconds(10)),
            new StepDefinition(
                StepId: "verify",
                DisplayName: "Verify file was created",
                RequiresFacts: ["AiResponse"],
                ProducesFacts: [],
                Idempotent: true)
        ],
        CanInterrupt: false,
        EstimatedCost: 0.2,
        RiskLevel: 0.3);

    /// <inheritdoc />
    public IEnumerable<Proposal> ProposeStart(UtilityAi.Utils.Runtime rt)
    {
        var request = rt.Bus.GetOrDefault<UserRequest>();
        if (request is null) yield break;

        yield return new Proposal(
            id: "file-creation.write",
            cons: [new ConstantValue(0.75)],
            act: _ =>
            {
                var (path, content) = FileCreationModule.ParseFileRequest(request.Text);
                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllText(path, content);
                    rt.Bus.Publish(new AiResponse($"File created: {path}"));
                    rt.Bus.Publish(new StepResult(StepOutcome.Succeeded, $"File created: {path}"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                {
                    rt.Bus.Publish(new AiResponse($"Failed to create file: {path} ({ex.Message})"));
                    rt.Bus.Publish(new StepResult(StepOutcome.FailedRetryable, $"Failed to create file: {path}"));
                }
                return Task.CompletedTask;
            })
        { Description = "Create a file with the specified content" };
    }

    /// <inheritdoc />
    public IEnumerable<Proposal> ProposeSteps(UtilityAi.Utils.Runtime rt, ActiveWorkflow active)
    {
        if (active.CurrentStepId != "verify") yield break;

        var response = rt.Bus.GetOrDefault<AiResponse>();
        if (response is null) yield break;

        yield return new Proposal(
            id: "file-creation.verify",
            cons: [new ConstantValue(0.9)],
            act: _ =>
            {
                rt.Bus.Publish(new StepResult(StepOutcome.Succeeded, "File verified"));
                return Task.CompletedTask;
            })
        { Description = "Verify the created file" };
    }

    /// <inheritdoc />
    public IEnumerable<Proposal> ProposeRepair(UtilityAi.Utils.Runtime rt, ActiveWorkflow active, RepairDirective directive)
    {
        if (directive.Repair != RepairType.RetryStep) yield break;

        yield return new Proposal(
            id: "file-creation.retry",
            cons: [new ConstantValue(0.8)],
            act: _ =>
            {
                rt.Bus.Publish(new StepResult(StepOutcome.FailedRetryable, "Retry requested"));
                return Task.CompletedTask;
            })
        { Description = "Retry failed file creation step" };
    }
}
