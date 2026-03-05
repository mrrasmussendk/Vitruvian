using VitruvianAbstractions.Interfaces;
using Xunit;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VitruvianTests;

public sealed class ModelClientStructuredOutputExtensionsTests
{
    private sealed class CapturingModelClient(string responseText) : IModelClient
    {
        public ModelRequest? LastRequest { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(responseText);

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ModelResponse { Text = responseText });
        }

        public Task<string> CompleteAsync(
            string systemMessage,
            string userMessage,
            IReadOnlyList<ModelTool>? tools = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(responseText);
    }

    private sealed class ContactInfo
    {
        public required string Name { get; init; }
        public required string Email { get; init; }
        public bool DemoRequested { get; init; }
    }

    private sealed class ContactInfoWithOptionalNote
    {
        public required string Name { get; init; }
        public string? Notes { get; init; }
    }

    public sealed record GeneratedCommand(
        [property: JsonPropertyName("order")] int Order,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("purpose")] string Purpose,
        [property: JsonPropertyName("parameters")] Dictionary<string, string>? Parameters = null);

    [Fact]
    public async Task GenerateStructuredAsync_DeserializesTypedOutput_AndInjectsSchemaInstructions()
    {
        var client = new CapturingModelClient("""{"name":"John Smith","email":"john@example.com","demoRequested":true}""");

        var result = await client.GenerateStructuredAsync<ContactInfo>("Extract contact details");

        Assert.Equal("John Smith", result.Name);
        Assert.Equal("john@example.com", result.Email);
        Assert.True(result.DemoRequested);
        Assert.NotNull(client.LastRequest);
        Assert.Equal("Extract contact details", client.LastRequest!.Prompt);
        Assert.Contains("JSON Schema", client.LastRequest.SystemMessage);
        Assert.Contains("\"Name\"", client.LastRequest.SystemMessage);
        Assert.Contains("\"Email\"", client.LastRequest.SystemMessage);
    }

    [Fact]
    public async Task GenerateStructuredAsync_MarkdownWrappedJson_ParsesSuccessfully()
    {
        var client = new CapturingModelClient(
            """
            ```json
            {"name":"Jane Doe","email":"jane@example.com","demoRequested":false}
            ```
            """);

        var result = await client.GenerateStructuredAsync<ContactInfo>("Extract contact details");

        Assert.Equal("Jane Doe", result.Name);
        Assert.Equal("jane@example.com", result.Email);
        Assert.False(result.DemoRequested);
    }

    [Fact]
    public async Task GenerateStructuredAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        var client = new CapturingModelClient("not json");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GenerateStructuredAsync<ContactInfo>("Extract contact details"));

        Assert.Contains("Failed to parse structured output", ex.Message);
    }

    [Fact]
    public async Task GenerateStructuredAsync_NullableProperties_AreNotMarkedRequiredInSchema()
    {
        var client = new CapturingModelClient("""{"name":"Jane","notes":null}""");

        _ = await client.GenerateStructuredAsync<ContactInfoWithOptionalNote>("Extract contact details");

        Assert.NotNull(client.LastRequest);
        var schemaMarker = "JSON Schema:\n";
        var index = client.LastRequest!.SystemMessage!.IndexOf(schemaMarker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var schemaJson = client.LastRequest.SystemMessage[(index + schemaMarker.Length)..];
        var schema = JsonNode.Parse(schemaJson)!.AsObject();
        var required = schema["required"]!.AsArray().Select(static item => item!.GetValue<string>()).ToList();

        Assert.Contains("Name", required);
        Assert.DoesNotContain("Notes", required);
    }

    [Fact]
    public async Task GenerateStructuredAsync_JsonPropertyNameAndDictionary_AreRepresentedInSchema()
    {
        var client = new CapturingModelClient(
            """{"order":1,"command":"copy","purpose":"copy files","parameters":{"source":"a.txt","target":"b.txt"}}""");

        var result = await client.GenerateStructuredAsync<GeneratedCommand>("Extract command");

        Assert.Equal(1, result.Order);
        Assert.Equal("copy", result.Command);
        Assert.Equal("copy files", result.Purpose);
        Assert.Equal("a.txt", result.Parameters!["source"]);
        Assert.NotNull(client.LastRequest);

        var schemaMarker = "JSON Schema:\n";
        var index = client.LastRequest!.SystemMessage!.IndexOf(schemaMarker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var schemaJson = client.LastRequest.SystemMessage[(index + schemaMarker.Length)..];
        var schema = JsonNode.Parse(schemaJson)!.AsObject();
        var properties = schema["properties"]!.AsObject();
        Assert.NotNull(properties["order"]);
        Assert.NotNull(properties["command"]);
        Assert.NotNull(properties["purpose"]);
        Assert.NotNull(properties["parameters"]);
        var parametersSchema = properties["parameters"]!.AsObject();
        Assert.Equal("object", parametersSchema["type"]!.GetValue<string>());
        Assert.Equal("string", parametersSchema["additionalProperties"]!["type"]!.GetValue<string>());
    }
}
