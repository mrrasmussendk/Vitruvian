using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Workflow;
using UtilityAi.Compass.StandardModules;
using UtilityAi.Utils;

namespace UtilityAi.Compass.Tests;

public class FileCreationWorkflowTests
{
    [Fact]
    public void Define_ReturnsCorrectWorkflowDefinition()
    {
        var wf = new FileCreationWorkflow();
        var def = wf.Define();

        Assert.Equal("file-creation", def.WorkflowId);
        Assert.Equal("Create File", def.DisplayName);
        Assert.Contains(GoalTag.Execute, def.Goals);
        Assert.Contains(Lane.Execute, def.Lanes);
        Assert.Equal(2, def.Steps.Length);
        Assert.Equal("write", def.Steps[0].StepId);
        Assert.Equal("verify", def.Steps[1].StepId);
        Assert.False(def.CanInterrupt);
        Assert.Equal(0.2, def.EstimatedCost);
        Assert.Equal(0.3, def.RiskLevel);
    }

    [Fact]
    public void ProposeStart_ReturnsProposal_WhenUserRequestExists()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        bus.Publish(new UserRequest("create file test.txt with content hello"));
        var rt = new UtilityAi.Utils.Runtime(bus, 0);

        var proposals = wf.ProposeStart(rt).ToList();

        Assert.Single(proposals);
        Assert.Equal("file-creation.write", proposals[0].Id);
    }

    [Fact]
    public void ProposeStart_ReturnsEmpty_WhenNoUserRequest()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        var rt = new UtilityAi.Utils.Runtime(bus, 0);

        var proposals = wf.ProposeStart(rt).ToList();

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task ProposeStart_ActCreatesFile_WhenExecuted()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.txt");

        try
        {
            bus.Publish(new UserRequest($"create file {filePath} with content hello world"));
            var rt = new UtilityAi.Utils.Runtime(bus, 0);

            var proposals = wf.ProposeStart(rt).ToList();
            Assert.Single(proposals);

            await proposals[0].Act(CancellationToken.None);

            Assert.True(File.Exists(filePath));
            Assert.Equal("hello world", File.ReadAllText(filePath));

            var stepResult = bus.GetOrDefault<StepResult>();
            Assert.NotNull(stepResult);
            Assert.Equal(StepOutcome.Succeeded, stepResult.Outcome);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ProposeStart_ActPublishesFailedRetryable_WhenPathIsDirectory()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            bus.Publish(new UserRequest($"create file {tempDir} with content hello world"));
            var rt = new UtilityAi.Utils.Runtime(bus, 0);

            var proposals = wf.ProposeStart(rt).ToList();
            Assert.Single(proposals);

            await proposals[0].Act(CancellationToken.None);

            var response = bus.GetOrDefault<AiResponse>();
            Assert.NotNull(response);
            Assert.Contains("Failed to create file", response.Text);

            var stepResult = bus.GetOrDefault<StepResult>();
            Assert.NotNull(stepResult);
            Assert.Equal(StepOutcome.FailedRetryable, stepResult.Outcome);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProposeSteps_ReturnsVerifyProposal_WhenOnVerifyStep()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        bus.Publish(new AiResponse("File created: test.txt"));
        var rt = new UtilityAi.Utils.Runtime(bus, 0);
        var active = new ActiveWorkflow("file-creation", "run-1", "verify", WorkflowStatus.Active);

        var proposals = wf.ProposeSteps(rt, active).ToList();

        Assert.Single(proposals);
        Assert.Equal("file-creation.verify", proposals[0].Id);
    }

    [Fact]
    public void ProposeSteps_ReturnsEmpty_WhenNotOnVerifyStep()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        var rt = new UtilityAi.Utils.Runtime(bus, 0);
        var active = new ActiveWorkflow("file-creation", "run-1", "write", WorkflowStatus.Active);

        Assert.Empty(wf.ProposeSteps(rt, active));
    }

    [Fact]
    public void ProposeRepair_ReturnsRetryProposal_ForRetryStep()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        var rt = new UtilityAi.Utils.Runtime(bus, 0);
        var active = new ActiveWorkflow("file-creation", "run-1", "write", WorkflowStatus.Repairing);
        var directive = new RepairDirective(RepairType.RetryStep, "IO error");

        var proposals = wf.ProposeRepair(rt, active, directive).ToList();

        Assert.Single(proposals);
        Assert.Equal("file-creation.retry", proposals[0].Id);
    }

    [Fact]
    public void ProposeRepair_ReturnsEmpty_ForNonRetryDirective()
    {
        var wf = new FileCreationWorkflow();
        var bus = new EventBus();
        var rt = new UtilityAi.Utils.Runtime(bus, 0);
        var active = new ActiveWorkflow("file-creation", "run-1", "write", WorkflowStatus.Repairing);
        var directive = new RepairDirective(RepairType.AskUser);

        Assert.Empty(wf.ProposeRepair(rt, active, directive));
    }
}
