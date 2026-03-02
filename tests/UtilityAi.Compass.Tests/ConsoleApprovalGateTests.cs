using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Hitl;
using Xunit;

namespace UtilityAi.Compass.Tests;

public sealed class ConsoleApprovalGateTests
{
    [Fact]
    public async Task ApproveAsync_UserTypesY_ReturnsTrue()
    {
        var input = new StringReader("y\n");
        var output = new StringWriter();
        var gate = new ConsoleApprovalGate(input: input, output: output);

        var result = await gate.ApproveAsync(
            OperationType.Write, "Write to file.txt", "file-operations");

        Assert.True(result);
    }

    [Fact]
    public async Task ApproveAsync_UserTypesN_ReturnsFalse()
    {
        var input = new StringReader("n\n");
        var output = new StringWriter();
        var gate = new ConsoleApprovalGate(input: input, output: output);

        var result = await gate.ApproveAsync(
            OperationType.Write, "Write to file.txt", "file-operations");

        Assert.False(result);
    }

    [Fact]
    public async Task ApproveAsync_UserTypesGarbage_ReturnsFalse()
    {
        var input = new StringReader("maybe\n");
        var output = new StringWriter();
        var gate = new ConsoleApprovalGate(input: input, output: output);

        var result = await gate.ApproveAsync(
            OperationType.Delete, "Delete database", "db-ops");

        Assert.False(result);
    }

    [Fact]
    public async Task ApproveAsync_RecordsAuditTrail()
    {
        var input = new StringReader("y\n");
        var output = new StringWriter();
        var gate = new ConsoleApprovalGate(input: input, output: output);

        await gate.ApproveAsync(
            OperationType.Write, "Write to file.txt", "file-operations");

        Assert.Single(gate.AuditLog);
        var record = gate.AuditLog[0];
        Assert.Equal("file-operations", record.ModuleDomain);
        Assert.Equal(OperationType.Write, record.Operation);
        Assert.Equal("Write to file.txt", record.Description);
        Assert.True(record.Approved);
    }

    [Fact]
    public async Task ApproveAsync_OutputContainsModuleInfo()
    {
        var input = new StringReader("n\n");
        var output = new StringWriter();
        var gate = new ConsoleApprovalGate(input: input, output: output);

        await gate.ApproveAsync(
            OperationType.Network, "Send email", "email-module");

        var text = output.ToString();
        Assert.Contains("email-module", text);
        Assert.Contains("Network", text);
        Assert.Contains("Send email", text);
    }

    [Fact]
    public async Task ApproveAsync_Timeout_DefaultDeny()
    {
        // Use a reader that returns empty immediately (simulates no input)
        var input = new StringReader("");
        var output = new StringWriter();
        var gate = new ConsoleApprovalGate(
            timeout: TimeSpan.FromMilliseconds(50),
            input: input,
            output: output);

        var result = await gate.ApproveAsync(
            OperationType.Write, "Write something", "writer");

        // ReadLine returns null when stream is empty, which != "y"
        Assert.False(result);
    }
}
