using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VitruvianAbstractions.Interfaces;

namespace VitruvianCli;

internal static class IntegrationDefaults
{
    public const int DefaultModelMaxTokens = 512;
    public const int DefaultDiscordPollIntervalSeconds = 2;
    public const int DefaultDiscordMessageLimit = 25;
    public const int MaxDiscordMessageLimit = 100;

    public static async Task EnsureSuccessAsync(string provider, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"{provider} API error {(int)response.StatusCode} ({response.StatusCode}): {errorBody}");
    }
}

public enum ModelProvider
{
    OpenAi,
    Anthropic,
    Gemini
}

public sealed record ModelConfiguration(ModelProvider Provider, string ApiKey, string Model)
{
    public static bool TryCreateFromEnvironment(out ModelConfiguration? configuration)
        => TryCreateFromEnvironment(out configuration, out _);

    public static bool TryCreateFromEnvironment(out ModelConfiguration? configuration, out string? error)
    {
        var providerText = Environment.GetEnvironmentVariable("VITRUVIAN_MODEL_PROVIDER");
        if (!TryParseProvider(providerText, out var provider))
        {
            configuration = null;
            error = "Unsupported VITRUVIAN_MODEL_PROVIDER. Supported values: openai, anthropic, gemini.";
            return false;
        }

        var (apiKeyVariable, defaultModel) = GetProviderDefaults(provider);

        var apiKey = Environment.GetEnvironmentVariable(apiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            configuration = null;
            error = $"Missing required API key environment variable: {apiKeyVariable}.";
            return false;
        }

        var model = Environment.GetEnvironmentVariable("VITRUVIAN_MODEL_NAME");
        configuration = new ModelConfiguration(provider, apiKey, string.IsNullOrWhiteSpace(model) ? defaultModel : model);
        error = null;
        return true;
    }

    public static bool TryParseProvider(string? value, out ModelProvider provider)
    {
        provider = ModelProvider.OpenAi;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalizedProvider = value.Trim().ToLowerInvariant();
        switch (normalizedProvider)
        {
            case "openai":
                provider = ModelProvider.OpenAi;
                return true;
            case "anthropic":
                provider = ModelProvider.Anthropic;
                return true;
            case "gemini":
                provider = ModelProvider.Gemini;
                return true;
            default:
                return false;
        }
    }

    private static (string ApiKeyVariable, string DefaultModel) GetProviderDefaults(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenAi => ("OPENAI_API_KEY", "gpt-4o-mini"),
        ModelProvider.Anthropic => ("ANTHROPIC_API_KEY", "claude-3-5-haiku-latest"),
        ModelProvider.Gemini => ("GEMINI_API_KEY", "gemini-2.0-flash"),
        _ => throw new NotSupportedException($"Unsupported model provider: {provider}.")
    };
}

public static class ModelClientFactory
{
    public static IModelClient Create(ModelConfiguration configuration, HttpClient httpClient) => configuration.Provider switch
    {
        ModelProvider.OpenAi => new OpenAiModelClient(configuration, httpClient),
        ModelProvider.Anthropic => new AnthropicModelClient(configuration, httpClient),
        ModelProvider.Gemini => new GeminiModelClient(configuration, httpClient),
        _ => throw new NotSupportedException($"Unsupported model provider: {configuration.Provider}.")
    };
}

file sealed class OpenAiModelClient(ModelConfiguration config, HttpClient httpClient) : IModelClient
{
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = await GenerateAsync(new ModelRequest { Prompt = prompt }, cancellationToken);
        return response.Text;
    }

    public async Task<string> CompleteAsync(string systemMessage, string userMessage, IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(new ModelRequest
        {
            Prompt = userMessage,
            SystemMessage = systemMessage,
            Tools = tools
        }, cancellationToken);
        return response.Text;
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest modelRequest, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            var body = new Dictionary<string, object>
            {
                ["model"] = modelRequest.ModelHint ?? config.Model,
                ["input"] = modelRequest.Prompt
            };

            if (!string.IsNullOrWhiteSpace(modelRequest.SystemMessage))
                body["instructions"] = modelRequest.SystemMessage;
            if (modelRequest.Tools is { Count: > 0 })
            {
                var mappedTools = BuildOpenAiTools(modelRequest.Tools);
                if (mappedTools.Count > 0)
                    body["tools"] = mappedTools;
            }

            // For reasoning models (GPT-5, o3), use low effort for faster responses
            var modelName = (modelRequest.ModelHint ?? config.Model).ToLowerInvariant();
            if (modelName.Contains("gpt-5") || modelName.Contains("o3") || modelName.Contains("o1"))
                body["reasoning"] = new Dictionary<string, object> { ["effort"] = "low" };

            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            await IntegrationDefaults.EnsureSuccessAsync("OpenAI", response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(payload);

            if (json.RootElement.TryGetProperty("output_text", out var outputText))
            {
                var text = outputText.GetString() ?? "OpenAI API returned empty content.";
                return new ModelResponse { Text = text };
            }

            if (!json.RootElement.TryGetProperty("output", out var output) || output.GetArrayLength() == 0)
                throw new InvalidOperationException("OpenAI API returned a response with missing or malformed output field.");

            var texts = new List<string>();
            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.GetString() == "message" &&
                    item.TryGetProperty("content", out var contentArray))
                {
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out var contentType) && contentType.GetString() == "output_text" &&
                            contentItem.TryGetProperty("text", out var textElement))
                        {
                            var textValue = textElement.GetString();
                            if (!string.IsNullOrEmpty(textValue))
                                texts.Add(textValue);
                        }
                    }
                }
            }

            if (texts.Count == 0)
                throw new InvalidOperationException("OpenAI API returned a response with no text output.");

            return new ModelResponse { Text = string.Join("\n", texts) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("Failed to call OpenAI API: The request timed out before completion.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to call OpenAI API: {ex.Message}", ex);
        }
    }

    private static List<object> BuildOpenAiTools(IReadOnlyList<ModelTool> tools)
    {
        var mapped = new List<object>(tools.Count);
        foreach (var tool in tools)
        {
            if (TryBuildMcpTool(tool, out var mcpTool))
            {
                mapped.Add(mcpTool);
                continue;
            }

            mapped.Add(new Dictionary<string, object>
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = BuildFunctionParametersSchema(tool.Parameters)
            });
        }

        return mapped;
    }

    private static Dictionary<string, object> BuildFunctionParametersSchema(IReadOnlyDictionary<string, string>? parameters)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        var required = new List<string>();

        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                required.Add(parameter.Key);
                var property = new Dictionary<string, object>
                {
                    ["type"] = "string"
                };
                if (!string.IsNullOrWhiteSpace(parameter.Value))
                    property["description"] = parameter.Value;

                properties[parameter.Key] = property;
            }
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    private static bool TryBuildMcpTool(ModelTool tool, out Dictionary<string, object> mcpTool)
    {
        mcpTool = new Dictionary<string, object>();
        var hasServerUrl = TryGetParameter(tool.Parameters, "server_url", out var serverUrl);
        var hasConnectorId = TryGetParameter(tool.Parameters, "connector_id", out var connectorId);
        var isMcp =
            hasServerUrl ||
            hasConnectorId ||
            (TryGetParameter(tool.Parameters, "type", out var toolType) && string.Equals(toolType, "mcp", StringComparison.OrdinalIgnoreCase));
        if (!isMcp || (!hasServerUrl && !hasConnectorId))
            return false;

        mcpTool["type"] = "mcp";
        mcpTool["server_label"] = TryGetParameter(tool.Parameters, "server_label", out var serverLabel) ? serverLabel : tool.Name;

        var serverDescription = TryGetParameter(tool.Parameters, "server_description", out var descriptionOverride)
            ? descriptionOverride
            : tool.Description;
        if (!string.IsNullOrWhiteSpace(serverDescription))
            mcpTool["server_description"] = serverDescription;
        if (hasServerUrl)
            mcpTool["server_url"] = serverUrl;
        if (hasConnectorId)
            mcpTool["connector_id"] = connectorId;
        if (TryGetParameter(tool.Parameters, "authorization", out var authorization))
            mcpTool["authorization"] = authorization;
        if (TryGetParameter(tool.Parameters, "require_approval", out var requireApproval))
            mcpTool["require_approval"] = ParseRequireApprovalValue(requireApproval);
        if (TryGetParameter(tool.Parameters, "allowed_tools", out var allowedTools))
        {
            var toolNames = ParseAllowedTools(allowedTools);
            if (toolNames.Length > 0)
                mcpTool["allowed_tools"] = toolNames;
        }

        return true;
    }

    private static bool TryGetParameter(IReadOnlyDictionary<string, string>? parameters, string key, out string value)
    {
        value = string.Empty;
        if (parameters is null)
            return false;

        foreach (var parameter in parameters)
        {
            if (string.Equals(parameter.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(parameter.Value))
            {
                value = parameter.Value;
                return true;
            }
        }

        return false;
    }

    private static object ParseRequireApprovalValue(string value)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, "always", StringComparison.OrdinalIgnoreCase))
            return "always";
        if (string.Equals(normalized, "never", StringComparison.OrdinalIgnoreCase))
            return "never";

        if (normalized.StartsWith('{') || normalized.StartsWith('['))
        {
            try
            {
                using var parsed = JsonDocument.Parse(normalized);
                return parsed.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid MCP require_approval JSON payload.", ex);
            }
        }

        return normalized;
    }

    private static string[] ParseAllowedTools(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('['))
        {
            try
            {
                using var parsed = JsonDocument.Parse(normalized);
                return parsed.RootElement.ValueKind == JsonValueKind.Array
                    ? parsed.RootElement.EnumerateArray()
                        .Where(element => element.ValueKind == JsonValueKind.String)
                        .Select(element => element.GetString())
                        .OfType<string>()
                        .Where(static element => !string.IsNullOrWhiteSpace(element))
                        .ToArray()
                    : [];
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid MCP allowed_tools JSON payload.", ex);
            }
        }

        return normalized
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}

file sealed class AnthropicModelClient(ModelConfiguration config, HttpClient httpClient) : IModelClient
{
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = await GenerateAsync(new ModelRequest { Prompt = prompt }, cancellationToken);
        return response.Text;
    }

    public async Task<string> CompleteAsync(string systemMessage, string userMessage, IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(new ModelRequest
        {
            Prompt = userMessage,
            SystemMessage = systemMessage,
            Tools = tools
        }, cancellationToken);
        return response.Text;
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest modelRequest, CancellationToken cancellationToken)
    {
        try
        {
            var maxTokens = modelRequest.MaxTokens
                ?? (int.TryParse(Environment.GetEnvironmentVariable("VITRUVIAN_MODEL_MAX_TOKENS"), out var configuredMaxTokens)
                    ? configuredMaxTokens
                    : IntegrationDefaults.DefaultModelMaxTokens);

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", config.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var body = new Dictionary<string, object>
            {
                ["model"] = modelRequest.ModelHint ?? config.Model,
                ["max_tokens"] = maxTokens,
                ["messages"] = new[] { new { role = "user", content = modelRequest.Prompt } }
            };
            if (!string.IsNullOrWhiteSpace(modelRequest.SystemMessage))
                body["system"] = modelRequest.SystemMessage;
            if (modelRequest.Temperature.HasValue)
                body["temperature"] = modelRequest.Temperature.Value;

            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            await IntegrationDefaults.EnsureSuccessAsync("Anthropic", response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(payload);

            if (!json.RootElement.TryGetProperty("content", out var contentArray) ||
                contentArray.GetArrayLength() == 0 ||
                !contentArray[0].TryGetProperty("text", out var textProperty))
            {
                throw new InvalidOperationException("Anthropic API returned a response with missing or malformed text field.");
            }

            var text = textProperty.GetString() ?? "Anthropic API returned empty text.";
            return new ModelResponse { Text = text };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("Failed to call Anthropic API: The request timed out before completion.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to call Anthropic API: {ex.Message}", ex);
        }
    }
}

file sealed class GeminiModelClient(ModelConfiguration config, HttpClient httpClient) : IModelClient
{
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = await GenerateAsync(new ModelRequest { Prompt = prompt }, cancellationToken);
        return response.Text;
    }

    public async Task<string> CompleteAsync(string systemMessage, string userMessage, IReadOnlyList<ModelTool>? tools = null, CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(new ModelRequest
        {
            Prompt = userMessage,
            SystemMessage = systemMessage,
            Tools = tools
        }, cancellationToken);
        return response.Text;
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest modelRequest, CancellationToken cancellationToken)
    {
        try
        {
            var model = modelRequest.ModelHint ?? config.Model;
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", config.ApiKey);

            var parts = new List<object>();
            parts.Add(new { text = modelRequest.Prompt });

            var body = new Dictionary<string, object>
            {
                ["contents"] = new[] { new { parts } }
            };
            if (!string.IsNullOrWhiteSpace(modelRequest.SystemMessage))
                body["systemInstruction"] = new { parts = new[] { new { text = modelRequest.SystemMessage } } };
            if (modelRequest.Temperature.HasValue || modelRequest.MaxTokens.HasValue)
            {
                var generationConfig = new Dictionary<string, object>();
                if (modelRequest.Temperature.HasValue)
                    generationConfig["temperature"] = modelRequest.Temperature.Value;
                if (modelRequest.MaxTokens.HasValue)
                    generationConfig["maxOutputTokens"] = modelRequest.MaxTokens.Value;
                body["generationConfig"] = generationConfig;
            }

            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            await IntegrationDefaults.EnsureSuccessAsync("Gemini", response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(payload);

            if (!json.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0 ||
                !candidates[0].TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var partsArray) ||
                partsArray.GetArrayLength() == 0 ||
                !partsArray[0].TryGetProperty("text", out var textProperty))
            {
                throw new InvalidOperationException("Gemini API returned a response with missing or malformed text field.");
            }

            var text = textProperty.GetString() ?? "Gemini API returned empty text.";
            return new ModelResponse { Text = text };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("Failed to call Gemini API: The request timed out before completion.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to call Gemini API: {ex.Message}", ex);
        }
    }
}
