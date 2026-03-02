using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.PluginHost;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class SandboxedModuleRunnerTests
{
    private sealed class SlowModule : ICompassModule
    {
        public string Domain => "slow";
        public string Description => "A module that takes too long";

        public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return "done";
        }
    }

    private sealed class FastModule : ICompassModule
    {
        public string Domain => "fast";
        public string Description => "A fast module";

        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult("fast result");
    }

    [Fact]
    public async Task ExecuteAsync_FastModule_ReturnsResult()
    {
        var policy = new DefaultSandboxPolicy { MaxWallTime = TimeSpan.FromSeconds(5) };
        var runner = new SandboxedModuleRunner(policy);
        var module = new FastModule();

        var result = await runner.ExecuteAsync(module, "test", null);

        Assert.Equal("fast result", result);
    }

    [Fact]
    public async Task ExecuteAsync_SlowModule_ThrowsTimeoutException()
    {
        var policy = new DefaultSandboxPolicy { MaxWallTime = TimeSpan.FromMilliseconds(50) };
        var runner = new SandboxedModuleRunner(policy);
        var module = new SlowModule();

        await Assert.ThrowsAsync<TimeoutException>(
            () => runner.ExecuteAsync(module, "test", null));
    }

    [Fact]
    public void CreateIsolatedContext_ReturnsCollectibleContext()
    {
        var ctx = SandboxedModuleRunner.CreateIsolatedContext("TestPlugin.dll");

        Assert.True(ctx.IsCollectible);
        Assert.Contains("TestPlugin", ctx.Name);

        ctx.Unload();
    }

    [Fact]
    public void DefaultSandboxPolicy_HasSensibleDefaults()
    {
        var policy = new DefaultSandboxPolicy();

        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxCpuTime);
        Assert.Equal(256 * 1024 * 1024, policy.MaxMemoryBytes);
        Assert.Equal(TimeSpan.FromSeconds(60), policy.MaxWallTime);
        Assert.False(policy.AllowFileSystem);
        Assert.False(policy.AllowNetwork);
        Assert.False(policy.AllowProcessSpawn);
    }
}
