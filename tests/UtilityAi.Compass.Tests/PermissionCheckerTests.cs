using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Interfaces;
using UtilityAi.Compass.PluginSdk.Attributes;
using UtilityAi.Compass.Runtime;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class PermissionCheckerTests
{
    private sealed class TestPermissionContext : IPermissionContext
    {
        public string UserId { get; init; } = "owner";
        public string Group { get; init; } = "default";
        public ModuleAccess UserPermissions { get; init; } = ModuleAccess.Read | ModuleAccess.Write | ModuleAccess.Execute;
        public ModuleAccess GroupPermissions { get; init; } = ModuleAccess.Read;
        public ModuleAccess OtherPermissions { get; init; } = ModuleAccess.None;

        public bool HasPermission(string userId, string group, ModuleAccess access)
        {
            if (userId == UserId)
                return (UserPermissions & access) == access;
            if (group == Group)
                return (GroupPermissions & access) == access;
            return (OtherPermissions & access) == access;
        }
    }

    [RequiresPermission(ModuleAccess.Read)]
    private sealed class ReadOnlyModule : ICompassModule
    {
        public string Domain => "read-only";
        public string Description => "Read-only module";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult("ok");
    }

    [RequiresPermission(ModuleAccess.Write)]
    private sealed class WriteModule : ICompassModule
    {
        public string Domain => "writer";
        public string Description => "Write module";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult("ok");
    }

    [RequiresPermission(ModuleAccess.Read)]
    [RequiresPermission(ModuleAccess.Write)]
    private sealed class ReadWriteModule : ICompassModule
    {
        public string Domain => "read-write";
        public string Description => "Read-write module";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult("ok");
    }

    private sealed class NoPermissionModule : ICompassModule
    {
        public string Domain => "open";
        public string Description => "No permissions required";
        public Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
            => Task.FromResult("ok");
    }

    [Fact]
    public void IsAllowed_OwnerWithFullAccess_ReturnsTrue()
    {
        var ctx = new TestPermissionContext();
        var checker = new PermissionChecker(ctx);

        Assert.True(checker.IsAllowed(new WriteModule(), "owner", "default"));
    }

    [Fact]
    public void IsAllowed_GroupWithReadOnly_DeniesWrite()
    {
        var ctx = new TestPermissionContext();
        var checker = new PermissionChecker(ctx);

        Assert.False(checker.IsAllowed(new WriteModule(), "other-user", "default"));
    }

    [Fact]
    public void IsAllowed_OtherWithNoAccess_DeniesRead()
    {
        var ctx = new TestPermissionContext();
        var checker = new PermissionChecker(ctx);

        Assert.False(checker.IsAllowed(new ReadOnlyModule(), "stranger", "strangers"));
    }

    [Fact]
    public void IsAllowed_ModuleWithoutAttribute_AlwaysAllowed()
    {
        var ctx = new TestPermissionContext { OtherPermissions = ModuleAccess.None };
        var checker = new PermissionChecker(ctx);

        Assert.True(checker.IsAllowed(new NoPermissionModule(), "stranger", "strangers"));
    }

    [Fact]
    public void Enforce_ThrowsPermissionDeniedException_WhenDenied()
    {
        var ctx = new TestPermissionContext();
        var checker = new PermissionChecker(ctx);

        var ex = Assert.Throws<PermissionDeniedException>(
            () => checker.Enforce(new WriteModule(), "stranger", "strangers"));

        Assert.Equal("writer", ex.ModuleDomain);
        Assert.Equal(ModuleAccess.Write, ex.RequiredAccess);
    }

    [Fact]
    public void Enforce_DoesNotThrow_WhenAllowed()
    {
        var ctx = new TestPermissionContext();
        var checker = new PermissionChecker(ctx);

        checker.Enforce(new ReadOnlyModule(), "owner", "default");
    }

    [Fact]
    public void GetRequiredAccess_CombinesMultipleAttributes()
    {
        var access = PermissionChecker.GetRequiredAccess(typeof(ReadWriteModule));

        Assert.True(access.HasFlag(ModuleAccess.Read));
        Assert.True(access.HasFlag(ModuleAccess.Write));
        Assert.False(access.HasFlag(ModuleAccess.Execute));
    }

    [Fact]
    public void GetRequiredAccess_ReturnsNone_WhenNoAttribute()
    {
        var access = PermissionChecker.GetRequiredAccess(typeof(NoPermissionModule));

        Assert.Equal(ModuleAccess.None, access);
    }
}
