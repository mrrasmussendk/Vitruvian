using System.Net;
using System.Text;
using System.Text.Json;
using VitruvianAbstractions;
using VitruvianAbstractions.Interfaces;
using VitruvianCli;
using Xunit;

namespace VitruvianTests;

public sealed class OpenAiModelClientToolMappingTests
{
    private const string OpenAiOkPayload = """{"output_text":"ok"}""";
    private const string OpenAiMcpApprovalPayload = """
                                                    {
                                                      "id": "resp_approval",
                                                      "output": [
                                                        {
                                                          "id": "apr_123",
                                                          "type": "mcp_approval_request",
                                                          "name": "roll",
                                                          "server_label": "dmcp",
                                                          "arguments": "{\"diceRollExpression\":\"2d4+1\"}"
                                                       }
                                                     ]
                                                    }
                                                    """;
    private const string ClaudeMcpApprovalPayload = """
                                                    {
                                                      "id": "msg_approval",
                                                      "content": [
                                                        {
                                                          "type": "mcp_approval_request",
                                                          "id": "claude_apr_1",
                                                          "name": "roll",
                                                          "server_label": "dmcp"
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
        Assert.Contains("no HITL approval gate is configured", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_WhenMcpApprovalRequestAndHitlApproves_SendsApprovalResponseAndReturnsText()
    {
        var handler = new CapturingHttpMessageHandler(OpenAiMcpApprovalPayload, """{"output_text":"approved and completed"}""");
        using var httpClient = new HttpClient(handler);
        var approvalGate = new TestApprovalGate(approved: true);
        var client = ModelClientFactory.Create(new ModelConfiguration(ModelProvider.OpenAi, "test-key", "gpt-4o-mini"), httpClient, approvalGate);

        var response = await client.GenerateAsync(new ModelRequest
        {
            Prompt = "Roll 2d4+1",
            Tools =
            [
                new ModelTool("dmcp", "Dice server", new Dictionary<string, string>
                {
                    ["type"] = "mcp",
                    ["server_url"] = "https://dmcp-server.deno.dev/sse",
                    ["require_approval"] = "always"
                })
            ]
        });

        Assert.Equal("approved and completed", response.Text);
        Assert.Equal(1, approvalGate.Calls);
        Assert.Equal(2, handler.RequestBodies.Count);

        using var followUpJson = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Equal("resp_approval", followUpJson.RootElement.GetProperty("previous_response_id").GetString());
        var approvalInput = followUpJson.RootElement.GetProperty("input")[0];
        Assert.Equal("mcp_approval_response", approvalInput.GetProperty("type").GetString());
        Assert.True(approvalInput.GetProperty("approve").GetBoolean());
        Assert.Equal("apr_123", approvalInput.GetProperty("approval_request_id").GetString());
    }

    [Fact]
    public async Task GenerateAsync_WhenClaudeReturnsMcpApprovalRequest_ThrowsHelpfulErrorWithoutHitl()
    {
        var handler = new CapturingHttpMessageHandler(ClaudeMcpApprovalPayload);
        using var httpClient = new HttpClient(handler);
        var client = ModelClientFactory.Create(new ModelConfiguration(ModelProvider.Anthropic, "test-key", "claude-3-5-haiku-latest"), httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(new ModelRequest { Prompt = "Roll 2d4+1" }));
        Assert.Contains("Claude MCP approval is required", ex.Message);
        Assert.Contains("no HITL approval gate is configured", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_WhenClaudeApprovalRequestAndHitlApproves_SendsApprovalResponseAndReturnsText()
    {
        var handler = new CapturingHttpMessageHandler(ClaudeMcpApprovalPayload, """{"content":[{"type":"text","text":"claude approved and completed"}]}""");
        using var httpClient = new HttpClient(handler);
        var approvalGate = new TestApprovalGate(approved: true);
        var client = ModelClientFactory.Create(new ModelConfiguration(ModelProvider.Anthropic, "test-key", "claude-3-5-haiku-latest"), httpClient, approvalGate);

        var response = await client.GenerateAsync(new ModelRequest { Prompt = "Roll 2d4+1" });

        Assert.Equal("claude approved and completed", response.Text);
        Assert.Equal(1, approvalGate.Calls);
        Assert.Equal(2, handler.RequestBodies.Count);

        using var followUpJson = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = followUpJson.RootElement.GetProperty("messages");
        var approvalResponse = messages[2].GetProperty("content")[0];
        Assert.Equal("mcp_approval_response", approvalResponse.GetProperty("type").GetString());
        Assert.True(approvalResponse.GetProperty("approve").GetBoolean());
        Assert.Equal("claude_apr_1", approvalResponse.GetProperty("approval_request_id").GetString());
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responsePayloads;

        public CapturingHttpMessageHandler(params string[] responsePayloads)
        {
            if (responsePayloads is null || responsePayloads.Length == 0)
                throw new ArgumentException("At least one response payload is required.", nameof(responsePayloads));

            _responsePayloads = new Queue<string>(responsePayloads);
        }

        public string? LastRequestBody { get; private set; }
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (LastRequestBody is not null)
                RequestBodies.Add(LastRequestBody);

            var payload = _responsePayloads.Count > 1 ? _responsePayloads.Dequeue() : _responsePayloads.Peek();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestApprovalGate(bool approved) : IApprovalGate
    {
        public int Calls { get; private set; }

        public Task<bool> ApproveAsync(OperationType operation, string description, string moduleDomain, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(approved);
        }
    }
}
