using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class CliHostedServiceTests
{
    [Theory]
    [InlineData("/schedule \"every 5 minutes\" check weather", "every 5 minutes", "check weather")]
    [InlineData("/schedule \"daily\" send summary email", "daily", "send summary email")]
    [InlineData("/schedule \"every 30 seconds\" ping server", "every 30 seconds", "ping server")]
    public void TryParseScheduleCommand_ValidInput_ReturnsTrue(string input, string expectedSchedule, string expectedTask)
    {
        var result = CliHostedService.TryParseScheduleCommand(input, out var schedule, out var task);

        Assert.True(result);
        Assert.Equal(expectedSchedule, schedule);
        Assert.Equal(expectedTask, task);
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/schedule")]
    [InlineData("/schedule \"\"")]
    [InlineData("/schedule \"every 5 minutes\"")]  // no task after schedule
    [InlineData("schedule \"every 5 minutes\" test")]  // missing /
    public void TryParseScheduleCommand_InvalidInput_ReturnsFalse(string input)
    {
        var result = CliHostedService.TryParseScheduleCommand(input, out _, out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData("/unregister-module conversation", "conversation")]
    [InlineData("/unregister-module file-operations", "file-operations")]
    [InlineData("/unregister-module  web-search ", "web-search")]
    [InlineData("/UNREGISTER-MODULE my-module", "my-module")]
    public void TryParseUnregisterCommand_ValidInput_ReturnsTrue(string input, string expectedDomain)
    {
        var result = CliHostedService.TryParseUnregisterCommand(input, out var domain);

        Assert.True(result);
        Assert.Equal(expectedDomain, domain);
    }

    [Theory]
    [InlineData("/unregister-module")]
    [InlineData("/unregister-module ")]
    [InlineData("/help")]
    [InlineData("unregister-module conversation")]  // missing /
    public void TryParseUnregisterCommand_InvalidInput_ReturnsFalse(string input)
    {
        var result = CliHostedService.TryParseUnregisterCommand(input, out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData("/load-module /tmp/MyModule.dll", "/tmp/MyModule.dll")]
    [InlineData("/load-module  C:\\dev\\MyModule.dll ", "C:\\dev\\MyModule.dll")]
    [InlineData("/LOAD-MODULE ./bin/Debug/net8.0/MyModule.dll", "./bin/Debug/net8.0/MyModule.dll")]
    public void TryParseLoadModuleCommand_ValidInput_ReturnsTrue(string input, string expectedPath)
    {
        var result = CliHostedService.TryParseLoadModuleCommand(input, out var modulePath);

        Assert.True(result);
        Assert.Equal(expectedPath, modulePath);
    }

    [Theory]
    [InlineData("/load-module")]
    [InlineData("/load-module ")]
    [InlineData("/help")]
    [InlineData("load-module /tmp/MyModule.dll")] // missing /
    public void TryParseLoadModuleCommand_InvalidInput_ReturnsFalse(string input)
    {
        var result = CliHostedService.TryParseLoadModuleCommand(input, out _);

        Assert.False(result);
    }
}
