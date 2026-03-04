using System.Text.Json;
using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class WebSocketChannelBridgeTests
{
    [Fact]
    public void ParseInbound_WithJsonPayload_ParsesDeveloperFields()
    {
        var message = WebSocketChannelBridge.ParseInbound("""{"request":"run status","domain":"ops","userId":"alice"}""");

        Assert.Equal("run status", message.Request);
        Assert.Equal("ops", message.Domain);
        Assert.Equal("alice", message.UserId);
    }

    [Fact]
    public void ToProcessorInput_UsesDomainTag_WhenDomainIsProvided()
    {
        var input = WebSocketChannelBridge.ToProcessorInput(new WebSocketInboundMessage("list incidents", "support"), fallbackDomain: null);

        Assert.Equal("[domain:support] list incidents", input);
    }

    [Fact]
    public void BuildOutboundPayload_IncludesHelpfulSchemaHint()
    {
        var payload = WebSocketChannelBridge.BuildOutboundPayload("ok", new WebSocketInboundMessage("ping", "web"), fallbackDomain: null);
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        Assert.Equal("ok", root.GetProperty("response").GetString());
        Assert.Equal("web", root.GetProperty("domain").GetString());
        Assert.Contains("domain", root.GetProperty("helper").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
