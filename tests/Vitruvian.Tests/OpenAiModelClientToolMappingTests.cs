using System.Net;
using System.Text;
using System.Text.Json;
using VitruvianAbstractions.Interfaces;
using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class OpenAiModelClientToolMappingTests
{
    private const string OpenAiOkPayload = """{"output_text":"ok"}""";
    private const string OpenAiMcpApprovalPayload = """
                                                   {
                                                     "output": [
                                                       {
                                                         "type": "mcp_approval_request",
                                                         "name": "roll",
                                                         "server_label": "dmcp",
                                                         "arguments": "{\"diceRollExpression\":\"2d4+1\"}"
                                                       }
                                                     ]
                                                   }
                                                   """;

    [Fact]
    public async Task GenerateAsync_WithFunctionTool_MapsToolToOpenAiFunctionSchema()
    {
        var handler = new CapturingHttpMessageHandler(OpenAiOkPayload);
        using var httpClient = new HttpClient(handler);
        var client = ModelClientFactory.Create(new ModelConfiguration(ModelProvider.OpenAi, "test-key", "gpt-4o-mini"), httpClient);

        await client.GenerateAsync(new ModelRequest
        {
            Prompt = "Use the add tool.",
            Tools =
            [
                new ModelTool("add", "Add two numbers", new Dictionary<string, string>
                {
                    ["a"] = "First number",
                    ["b"] = "Second number"
                })
            ]
        });

        Assert.NotNull(handler.LastRequestBody);
        using var json = JsonDocument.Parse(handler.LastRequestBody!);
        var tool = json.RootElement.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("add", tool.GetProperty("name").GetString());
        Assert.Equal("Add two numbers", tool.GetProperty("description").GetString());
        Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.Equal("string", tool.GetProperty("parameters").GetProperty("properties").GetProperty("a").GetProperty("type").GetString());
        Assert.Equal("First number", tool.GetProperty("parameters").GetProperty("properties").GetProperty("a").GetProperty("description").GetString());
        Assert.Equal("a", tool.GetProperty("parameters").GetProperty("required")[0].GetString());
        Assert.Equal("b", tool.GetProperty("parameters").GetProperty("required")[1].GetString());
    }

    [Fact]
    public async Task GenerateAsync_WithMcpServerAndConnectorTools_MapsMcpFields()
    {
        var handler = new CapturingHttpMessageHandler(OpenAiOkPayload);
        using var httpClient = new HttpClient(handler);
        var client = ModelClientFactory.Create(new ModelConfiguration(ModelProvider.OpenAi, "test-key", "gpt-4o-mini"), httpClient);

        await client.GenerateAsync(new ModelRequest
        {
            Prompt = "Use MCP tools.",
            Tools =
            [
                new ModelTool("dmcp", "Dice server", new Dictionary<string, string>
                {
                    ["type"] = "mcp",
                    ["server_url"] = "https://dmcp-server.deno.dev/sse",
                    ["require_approval"] = "never",
                    ["allowed_tools"] = "roll"
                }),
                new ModelTool("google_calendar", "Google Calendar connector", new Dictionary<string, string>
                {
                    ["type"] = "mcp",
                    ["connector_id"] = "connector_googlecalendar",
                    ["authorization"] = "token-value",
                    ["require_approval"] = """{"never":{"tool_names":["search_events"]}}"""
                })
            ]
        });

        Assert.NotNull(handler.LastRequestBody);
        using var json = JsonDocument.Parse(handler.LastRequestBody!);
        var tools = json.RootElement.GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());

        var remoteServerTool = tools[0];
        Assert.Equal("mcp", remoteServerTool.GetProperty("type").GetString());
        Assert.Equal("dmcp", remoteServerTool.GetProperty("server_label").GetString());
        Assert.Equal("Dice server", remoteServerTool.GetProperty("server_description").GetString());
        Assert.Equal("https://dmcp-server.deno.dev/sse", remoteServerTool.GetProperty("server_url").GetString());
        Assert.Equal("never", remoteServerTool.GetProperty("require_approval").GetString());
        Assert.Equal("roll", remoteServerTool.GetProperty("allowed_tools")[0].GetString());

        var connectorTool = tools[1];
        Assert.Equal("mcp", connectorTool.GetProperty("type").GetString());
        Assert.Equal("google_calendar", connectorTool.GetProperty("server_label").GetString());
        Assert.Equal("connector_googlecalendar", connectorTool.GetProperty("connector_id").GetString());
        Assert.Equal("token-value", connectorTool.GetProperty("authorization").GetString());
        Assert.Equal("search_events", connectorTool.GetProperty("require_approval").GetProperty("never").GetProperty("tool_names")[0].GetString());
    }

    [Fact]
    public async Task GenerateAsync_WhenOpenAiReturnsMcpApprovalRequest_ThrowsHelpfulError()
    {
        var handler = new CapturingHttpMessageHandler(OpenAiMcpApprovalPayload);
        using var httpClient = new HttpClient(handler);
        var client = ModelClientFactory.Create(new ModelConfiguration(ModelProvider.OpenAi, "test-key", "gpt-4o-mini"), httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(new ModelRequest { Prompt = "Roll 2d4+1" }));
        Assert.Contains("MCP approval is required", ex.Message);
        Assert.Contains("Set require_approval to 'never'", ex.Message);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responsePayload;

        public CapturingHttpMessageHandler(string responsePayload)
        {
            _responsePayload = responsePayload;
        }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responsePayload, Encoding.UTF8, "application/json")
            };
        }
    }
}
