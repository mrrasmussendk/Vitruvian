using VitruvianCli.Commands;
using Xunit;

namespace VitruvianTests;

public sealed class CommandRouterTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("/help")]
    [InlineData("--HELP")]
    public void TryRoute_HelpCommand_ReturnsTrue(string arg)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([arg], out var command);

        Assert.True(result);
        Assert.IsType<HelpCommand>(command);
    }

    [Theory]
    [InlineData("--list-modules")]
    [InlineData("/list-modules")]
    public void TryRoute_ListModulesCommand_ReturnsTrue(string arg)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([arg], out var command);

        Assert.True(result);
        Assert.IsType<ListModulesCommand>(command);
    }

    [Theory]
    [InlineData("--setup")]
    [InlineData("/setup")]
    public void TryRoute_SetupCommand_ReturnsTrue(string arg)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([arg], out var command);

        Assert.True(result);
        Assert.IsType<SetupCommand>(command);
    }

    [Theory]
    [InlineData("--configure-modules")]
    [InlineData("/configure-modules")]
    public void TryRoute_ConfigureModulesCommand_ReturnsTrue(string arg)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([arg], out var command);

        Assert.True(result);
        Assert.IsType<ConfigureModulesCommand>(command);
    }

    [Theory]
    [InlineData("--model", "openai")]
    [InlineData("/model", "anthropic")]
    [InlineData("--model", "gemini:gemini-pro")]
    public void TryRoute_ModelCommand_ReturnsTrue(string flag, string value)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, value], out var command);

        Assert.True(result);
        Assert.IsType<ModelCommand>(command);
    }

    [Theory]
    [InlineData("--install-module", "some-package@1.0")]
    [InlineData("/install-module", "/path/to/module.dll")]
    public void TryRoute_InstallModuleCommand_ReturnsTrue(string flag, string value)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, value], out var command);

        Assert.True(result);
        Assert.IsType<InstallModuleCommand>(command);
    }

    [Theory]
    [InlineData("inspect-module", "some-package")]
    [InlineData("--inspect-module", "some-package")]
    public void TryRoute_InspectModuleCommand_ReturnsTrue(string flag, string value)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, value], out var command);

        Assert.True(result);
        Assert.IsType<InspectModuleCommand>(command);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("--doctor")]
    public void TryRoute_DoctorCommand_ReturnsTrue(string arg)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([arg], out var command);

        Assert.True(result);
        Assert.IsType<DoctorCommand>(command);
    }

    [Theory]
    [InlineData("--new-module", "MyModule")]
    [InlineData("/new-module", "MyModule")]
    public void TryRoute_NewModuleCommand_ReturnsTrue(string flag, string value)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, value], out var command);

        Assert.True(result);
        Assert.IsType<NewModuleCommand>(command);
    }

    [Theory]
    [InlineData("policy", "validate", "policy.json")]
    [InlineData("--policy", "validate", "policy.json")]
    public void TryRoute_PolicyValidateCommand_ReturnsTrue(string flag, string sub, string file)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, sub, file], out var command);

        Assert.True(result);
        Assert.IsType<PolicyValidateCommand>(command);
    }

    [Theory]
    [InlineData("policy", "explain", "delete files")]
    [InlineData("--policy", "explain", "read data")]
    public void TryRoute_PolicyExplainCommand_ReturnsTrue(string flag, string sub, string request)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, sub, request], out var command);

        Assert.True(result);
        Assert.IsType<PolicyExplainCommand>(command);
    }

    [Theory]
    [InlineData("audit", "list")]
    [InlineData("--audit", "list")]
    public void TryRoute_AuditListCommand_ReturnsTrue(string flag, string sub)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, sub], out var command);

        Assert.True(result);
        Assert.IsType<AuditListCommand>(command);
    }

    [Theory]
    [InlineData("audit", "show", "123")]
    [InlineData("--audit", "show", "abc")]
    public void TryRoute_AuditShowCommand_ReturnsTrue(string flag, string sub, string id)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, sub, id], out var command);

        Assert.True(result);
        Assert.IsType<AuditShowCommand>(command);
    }

    [Theory]
    [InlineData("replay", "123")]
    [InlineData("--replay", "abc")]
    public void TryRoute_ReplayCommand_ReturnsTrue(string flag, string id)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([flag, id], out var command);

        Assert.True(result);
        Assert.IsType<ReplayCommand>(command);
    }

    [Fact]
    public void TryRoute_NoArgs_ReturnsFalse()
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([], out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData("unknown-command")]
    [InlineData("--nonexistent")]
    [InlineData("hello world")]
    public void TryRoute_UnknownCommand_ReturnsFalse(string arg)
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        var result = router.TryRoute([arg], out _);

        Assert.False(result);
    }

    [Fact]
    public void TryRoute_InstallModuleWithoutArg_ReturnsFalse()
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        // --install-module requires at least 2 args
        var result = router.TryRoute(["--install-module"], out _);

        Assert.False(result);
    }

    [Fact]
    public void TryRoute_ModelWithoutArg_ReturnsFalse()
    {
        var router = CommandRouter.CreateDefault("plugins", "modules");

        // --model requires at least 2 args
        var result = router.TryRoute(["--model"], out _);

        Assert.False(result);
    }
}
