using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.Abstractions.Planning;
using UtilityAi.Compass.Runtime.Planning;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class GoapPlannerTests
{
    private sealed class StubModelClient : IModelClient
    {
        private readonly string _response;

        public StubModelClient(string response) => _response = response;

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModelResponse { Text = _response });

        public Task<string> CompleteAsync(string systemMessage, string userMessage, IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);
    }

    [Fact]
    public async Task CreatePlanAsync_NoModules_ReturnsSingleStepWithEmptyDomain()
    {
        var planner = new GoapPlanner();

        var plan = await planner.CreatePlanAsync("do something", CancellationToken.None);

        Assert.Single(plan.Steps);
        Assert.Equal("", plan.Steps[0].ModuleDomain);
    }

    [Fact]
    public async Task CreatePlanAsync_NoModelClient_FallsBackToKeywordMatching()
    {
        var planner = new GoapPlanner();
        planner.RegisterModule("file-ops", "File operations for reading and writing files");

        var plan = await planner.CreatePlanAsync("read a file", CancellationToken.None);

        Assert.Single(plan.Steps);
        Assert.Equal("file-ops", plan.Steps[0].ModuleDomain);
    }

    [Fact]
    public async Task CreatePlanAsync_LlmReturnsSingleStep_ParsesCorrectly()
    {
        var json = """[{"step_id":"s1","module":"conversation","description":"Answer the question","input":"What is 2+2?","depends_on":[]}]""";
        var planner = new GoapPlanner(new StubModelClient(json));
        planner.RegisterModule("conversation", "General conversation");

        var plan = await planner.CreatePlanAsync("What is 2+2?", CancellationToken.None);

        Assert.Single(plan.Steps);
        Assert.Equal("s1", plan.Steps[0].StepId);
        Assert.Equal("conversation", plan.Steps[0].ModuleDomain);
        Assert.Empty(plan.Steps[0].DependsOn);
    }

    [Fact]
    public async Task CreatePlanAsync_LlmReturnsMultiStep_ParsesDependencies()
    {
        var json = """
        [
            {"step_id":"s1","module":"file-ops","description":"Read the file","input":"read notes.txt","depends_on":[]},
            {"step_id":"s2","module":"conversation","description":"Summarize content","input":"summarize the file content","depends_on":["s1"]}
        ]
        """;
        var planner = new GoapPlanner(new StubModelClient(json));
        planner.RegisterModule("file-ops", "File operations");
        planner.RegisterModule("conversation", "General conversation");

        var plan = await planner.CreatePlanAsync("read notes.txt then summarize it", CancellationToken.None);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Empty(plan.Steps[0].DependsOn);
        Assert.Single(plan.Steps[1].DependsOn);
        Assert.Equal("s1", plan.Steps[1].DependsOn[0]);
    }

    [Fact]
    public async Task CreatePlanAsync_LlmReturnsParallelSteps_NoDepsBetweenThem()
    {
        var json = """
        [
            {"step_id":"s1","module":"file-ops","description":"Read file A","input":"read a.txt","depends_on":[]},
            {"step_id":"s2","module":"file-ops","description":"Read file B","input":"read b.txt","depends_on":[]}
        ]
        """;
        var planner = new GoapPlanner(new StubModelClient(json));
        planner.RegisterModule("file-ops", "File operations");

        var plan = await planner.CreatePlanAsync("read a.txt and b.txt", CancellationToken.None);

        Assert.Equal(2, plan.Steps.Count);
        Assert.All(plan.Steps, step => Assert.Empty(step.DependsOn));
    }

    [Fact]
    public async Task CreatePlanAsync_LlmReturnsInvalidJson_FallsBackToSingleStep()
    {
        var planner = new GoapPlanner(new StubModelClient("not valid json at all"));
        planner.RegisterModule("conversation", "General conversation and questions");

        var plan = await planner.CreatePlanAsync("hello", CancellationToken.None);

        Assert.Single(plan.Steps);
    }

    [Fact]
    public async Task CreatePlanAsync_LlmReturnsUnknownModule_SkipsIt()
    {
        var json = """[{"step_id":"s1","module":"unknown-module","description":"Do something","input":"test","depends_on":[]}]""";
        var planner = new GoapPlanner(new StubModelClient(json));
        planner.RegisterModule("conversation", "General conversation");

        var plan = await planner.CreatePlanAsync("test", CancellationToken.None);

        // Unknown module is skipped, falls back to single step
        Assert.Single(plan.Steps);
        Assert.Equal("conversation", plan.Steps[0].ModuleDomain);
    }

    [Fact]
    public async Task CreatePlanAsync_LlmReturnsMarkdownWrappedJson_ParsesCorrectly()
    {
        var json = """
        ```json
        [{"step_id":"s1","module":"conversation","description":"Answer","input":"hello","depends_on":[]}]
        ```
        """;
        var planner = new GoapPlanner(new StubModelClient(json));
        planner.RegisterModule("conversation", "General conversation");

        var plan = await planner.CreatePlanAsync("hello", CancellationToken.None);

        Assert.Single(plan.Steps);
        Assert.Equal("conversation", plan.Steps[0].ModuleDomain);
    }

    [Fact]
    public async Task CreatePlanAsync_PlanIdIsUnique()
    {
        var planner = new GoapPlanner();
        planner.RegisterModule("conversation", "General conversation");

        var plan1 = await planner.CreatePlanAsync("hello", CancellationToken.None);
        var plan2 = await planner.CreatePlanAsync("world", CancellationToken.None);

        Assert.NotEqual(plan1.PlanId, plan2.PlanId);
    }

    [Fact]
    public async Task CreatePlanAsync_PreservesOriginalRequest()
    {
        var planner = new GoapPlanner();
        planner.RegisterModule("conversation", "General conversation");

        var plan = await planner.CreatePlanAsync("my original request", CancellationToken.None);

        Assert.Equal("my original request", plan.OriginalRequest);
    }
}
