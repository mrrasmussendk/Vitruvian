using System.Collections.Concurrent;
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Planning;
using UtilityAi.Compass.Runtime.Planning;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class PlanExecutorTests
{
    private sealed class TestModule : ICompassModule
    {
        public string Domain { get; }
        public string Description { get; }
        private readonly string _response;
        private readonly TimeSpan _delay;

        public int ExecutionCount;

        public TestModule(string domain, string description, string response = "ok", TimeSpan delay = default)
        {
            Domain = domain;
            Description = description;
            _response = response;
            _delay = delay;
        }

        public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
        {
            Interlocked.Increment(ref ExecutionCount);
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct);
            return _response;
        }
    }

    private sealed class FailingModule : ICompassModule
    {
        public string Domain => "failing";
        public string Description => "Always fails";

        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => throw new InvalidOperationException("Module failed");
    }

    private sealed class StubApprovalGate : IApprovalGate
    {
        private readonly bool _approve;
        public int ApprovalCount;

        public StubApprovalGate(bool approve) => _approve = approve;

        public Task<bool> ApproveAsync(OperationType operation, string description, string moduleDomain, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ApprovalCount);
            return Task.FromResult(_approve);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SingleStep_ReturnsModuleOutput()
    {
        var module = new TestModule("conversation", "General conversation", "Hello!");
        var modules = new Dictionary<string, ICompassModule> { ["conversation"] = module };
        var executor = new PlanExecutor(modules);

        var plan = new ExecutionPlan("p1", "hello", [
            new PlanStep("s1", "conversation", "Answer greeting", "hello", [])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.StepResults);
        Assert.Equal("Hello!", result.AggregatedOutput);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleSequentialSteps_ExecutesInOrder()
    {
        var readModule = new TestModule("file-ops", "File operations", "file content");
        var convModule = new TestModule("conversation", "Conversation", "summary of content");
        var modules = new Dictionary<string, ICompassModule>
        {
            ["file-ops"] = readModule,
            ["conversation"] = convModule
        };
        var executor = new PlanExecutor(modules);

        var plan = new ExecutionPlan("p1", "read and summarize", [
            new PlanStep("s1", "file-ops", "Read file", "read notes.txt", []),
            new PlanStep("s2", "conversation", "Summarize", "summarize content", ["s1"])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.StepResults.Count);
        Assert.Equal("file content", result.StepResults[0].Output);
        Assert.Equal("summary of content", result.StepResults[1].Output);
    }

    [Fact]
    public async Task ExecuteAsync_IndependentSteps_RunInParallel()
    {
        // Two independent steps with delays; if truly parallel, total time ~ delay, not 2×delay
        var moduleA = new TestModule("mod-a", "Module A", "result A", TimeSpan.FromMilliseconds(200));
        var moduleB = new TestModule("mod-b", "Module B", "result B", TimeSpan.FromMilliseconds(200));
        var modules = new Dictionary<string, ICompassModule>
        {
            ["mod-a"] = moduleA,
            ["mod-b"] = moduleB
        };
        var executor = new PlanExecutor(modules);

        var plan = new ExecutionPlan("p1", "do A and B", [
            new PlanStep("s1", "mod-a", "Step A", "do A", []),
            new PlanStep("s2", "mod-b", "Step B", "do B", [])
        ]);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);
        sw.Stop();

        Assert.True(result.Success);
        Assert.Equal(2, result.StepResults.Count);
        // Parallel: should complete in ~200ms, not ~400ms
        Assert.True(sw.ElapsedMilliseconds < 350, $"Expected parallel execution to finish in <350ms but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ExecuteAsync_FailingStep_MarksStepAsFailedButContinues()
    {
        var modules = new Dictionary<string, ICompassModule>
        {
            ["failing"] = new FailingModule(),
            ["conversation"] = new TestModule("conversation", "Conversation", "still works")
        };
        var executor = new PlanExecutor(modules);

        var plan = new ExecutionPlan("p1", "test", [
            new PlanStep("s1", "failing", "Will fail", "do something", []),
            new PlanStep("s2", "conversation", "Will succeed", "hello", [])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.False(result.Success); // Overall failure because s1 failed
        Assert.False(result.StepResults[0].Success);
        Assert.True(result.StepResults[1].Success);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownModule_ReportsError()
    {
        var modules = new Dictionary<string, ICompassModule>();
        var executor = new PlanExecutor(modules);

        var plan = new ExecutionPlan("p1", "test", [
            new PlanStep("s1", "nonexistent", "Unknown module step", "test", [])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No module found", result.StepResults[0].Output);
    }

    [Fact]
    public async Task ExecuteAsync_HitlApproves_StepExecutes()
    {
        var module = new TestModule("file-ops", "File operations", "file written");
        var modules = new Dictionary<string, ICompassModule> { ["file-ops"] = module };
        var gate = new StubApprovalGate(approve: true);
        var executor = new PlanExecutor(modules, gate);

        var plan = new ExecutionPlan("p1", "write file", [
            new PlanStep("s1", "file-ops", "Write a file", "create test.txt", [])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("file written", result.StepResults[0].Output);
        Assert.Equal(1, gate.ApprovalCount);
    }

    [Fact]
    public async Task ExecuteAsync_HitlDenies_StepBlocked()
    {
        var module = new TestModule("file-ops", "File operations", "file written");
        var modules = new Dictionary<string, ICompassModule> { ["file-ops"] = module };
        var gate = new StubApprovalGate(approve: false);
        var executor = new PlanExecutor(modules, gate);

        var plan = new ExecutionPlan("p1", "write file", [
            new PlanStep("s1", "file-ops", "Write a file", "create test.txt", [])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("denied by human reviewer", result.StepResults[0].Output);
    }

    [Fact]
    public async Task ExecuteAsync_ReadOnlyStep_BypassesHitl()
    {
        var module = new TestModule("conversation", "Conversation", "answer");
        var modules = new Dictionary<string, ICompassModule> { ["conversation"] = module };
        var gate = new StubApprovalGate(approve: false); // would deny if checked
        var executor = new PlanExecutor(modules, gate);

        var plan = new ExecutionPlan("p1", "question", [
            new PlanStep("s1", "conversation", "Answer a question", "What is AI?", [])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, gate.ApprovalCount); // Never called for read-only ops
    }

    [Fact]
    public async Task ExecuteAsync_CachesResults_SameInputNotReExecuted()
    {
        var module = new TestModule("conversation", "Conversation", "cached answer");
        var modules = new Dictionary<string, ICompassModule> { ["conversation"] = module };
        var executor = new PlanExecutor(modules);

        var plan1 = new ExecutionPlan("p1", "hello", [
            new PlanStep("s1", "conversation", "Answer", "hello", [])
        ]);
        var plan2 = new ExecutionPlan("p2", "hello", [
            new PlanStep("s1", "conversation", "Answer again", "hello", [])
        ]);

        await executor.ExecuteAsync(plan1, null, CancellationToken.None);
        var result = await executor.ExecuteAsync(plan2, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("cached answer", result.AggregatedOutput);
        Assert.Equal(1, module.ExecutionCount); // Only executed once due to caching
    }

    [Fact]
    public async Task ExecuteAsync_MemoryStoresPlanResults()
    {
        var module = new TestModule("conversation", "Conversation", "result");
        var modules = new Dictionary<string, ICompassModule> { ["conversation"] = module };
        var executor = new PlanExecutor(modules);

        var plan = new ExecutionPlan("p1", "test", [
            new PlanStep("s1", "conversation", "Test", "test", [])
        ]);

        await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.Single(executor.Memory);
        Assert.Equal("p1", executor.Memory[0].PlanId);
    }

    [Fact]
    public async Task ExecuteAsync_ContextWindow_InjectsRecentOutputs()
    {
        // Module that echoes its input so we can verify context injection
        var module = new EchoModule("echo", "Echo module");
        var modules = new Dictionary<string, ICompassModule> { ["echo"] = module };
        var executor = new PlanExecutor(modules, contextWindowSize: 2);

        var plan = new ExecutionPlan("p1", "test", [
            new PlanStep("s1", "echo", "Step 1", "first input", []),
            new PlanStep("s2", "echo", "Step 2", "second input", ["s1"])
        ]);

        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.True(result.Success);
        // Step 2 should have received context from step 1's output
        Assert.Contains("Prior step context", result.StepResults[1].Output);
    }

    private sealed class EchoModule : ICompassModule
    {
        public string Domain { get; }
        public string Description { get; }

        public EchoModule(string domain, string description)
        {
            Domain = domain;
            Description = description;
        }

        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult(request);
    }
}
