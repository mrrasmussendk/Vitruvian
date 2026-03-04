using System.Net.Http.Headers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VitruvianAbstractions.Interfaces;

namespace VitruvianCli;

file static class IntegrationDefaults
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
        var providerText = Environment.GetEnvironmentVariable("COMPASS_MODEL_PROVIDER");
        if (!TryParseProvider(providerText, out var provider))
        {
            configuration = null;
            error = "Unsupported COMPASS_MODEL_PROVIDER. Supported values: openai, anthropic, gemini.";
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

        var model = Environment.GetEnvironmentVariable("COMPASS_MODEL_NAME");
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
                ?? (int.TryParse(Environment.GetEnvironmentVariable("COMPASS_MODEL_MAX_TOKENS"), out var configuredMaxTokens)
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

public sealed class DiscordChannelBridge(HttpClient httpClient, string botToken, string channelId)
{
    private readonly int _pollIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("DISCORD_POLL_INTERVAL_SECONDS"), out var pollIntervalSeconds) && pollIntervalSeconds > 0
        ? pollIntervalSeconds
        : IntegrationDefaults.DefaultDiscordPollIntervalSeconds;
    private readonly int _messageLimit = int.TryParse(Environment.GetEnvironmentVariable("DISCORD_MESSAGE_LIMIT"), out var messageLimit) && messageLimit is > 0 and <= IntegrationDefaults.MaxDiscordMessageLimit
        ? messageLimit
        : IntegrationDefaults.DefaultDiscordMessageLimit;

    public async Task RunAsync(Func<string, CancellationToken, Task<string>> onUserMessage, CancellationToken cancellationToken)
    {
        try
        {
            var lastSeen = await GetNewestMessageIdAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var messages = await GetMessagesAsync(cancellationToken);
                foreach (var message in messages.OrderBy(m => m.Snowflake))
                {
                    if (lastSeen is not null && message.Snowflake <= lastSeen.Value)
                        continue;
                    if (message.IsBot || string.IsNullOrWhiteSpace(message.Content))
                        continue;

                    var reply = await onUserMessage(message.Content, cancellationToken);
                    await PostMessageAsync(reply, cancellationToken);
                    lastSeen = message.Snowflake;
                }

                await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<ulong?> GetNewestMessageIdAsync(CancellationToken cancellationToken)
    {
        var messages = await GetMessagesAsync(cancellationToken);
        return messages.OrderByDescending(m => m.Snowflake).FirstOrDefault()?.Snowflake;
    }

    private async Task<IReadOnlyList<DiscordMessage>> GetMessagesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v10/channels/{channelId}/messages?limit={_messageLimit}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(payload);
        var output = new List<DiscordMessage>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var author = item.GetProperty("author");
            var idValue = item.GetProperty("id").GetString() ?? string.Empty;
            if (!ulong.TryParse(idValue, out var snowflake))
                continue;

            output.Add(new DiscordMessage(
                snowflake,
                item.GetProperty("content").GetString() ?? string.Empty,
                author.TryGetProperty("bot", out var botNode) && botNode.GetBoolean()));
        }

        return output;
    }

    private async Task PostMessageAsync(string message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v10/channels/{channelId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { content = message }), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record DiscordMessage(ulong Snowflake, string Content, bool IsBot);
}

public sealed record WebSocketInboundMessage(string Request, string? Domain = null, string? UserId = null);

public sealed class WebSocketChannelBridge(string listenUrl, string? publicUrl = null, string? defaultDomain = null)
{
    private readonly string _listenerPrefix = NormalizeListenerPrefix(listenUrl);
    private readonly string _publicUrl = NormalizePublicUrl(publicUrl, listenUrl);

    public static string NormalizeListenerPrefix(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("WebSocket URL must be provided.", nameof(url));

        var normalized = url.Trim();
        if (normalized.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            normalized = $"http://{normalized[5..]}";
        else if (normalized.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            normalized = $"https://{normalized[6..]}";

        if (!normalized.EndsWith('/'))
            normalized += "/";
        return normalized;
    }

    public static string NormalizePublicUrl(string? publicUrl, string listenUrl)
    {
        if (!string.IsNullOrWhiteSpace(publicUrl))
            return publicUrl.Trim();

        if (listenUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return $"ws://{listenUrl[7..].TrimEnd('/')}/";
        if (listenUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return $"wss://{listenUrl[8..].TrimEnd('/')}/";

        return listenUrl.TrimEnd('/') + "/";
    }

    public static WebSocketInboundMessage ParseInbound(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new WebSocketInboundMessage(string.Empty);

        var trimmed = payload.Trim();
        if (!trimmed.StartsWith('{'))
            return new WebSocketInboundMessage(trimmed);

        try
        {
            using var json = JsonDocument.Parse(trimmed);
            var root = json.RootElement;
            var request = root.TryGetProperty("request", out var requestNode)
                ? requestNode.GetString()
                : root.TryGetProperty("message", out var messageNode)
                    ? messageNode.GetString()
                    : trimmed;
            var domain = root.TryGetProperty("domain", out var domainNode) ? domainNode.GetString() : null;
            var userId = root.TryGetProperty("userId", out var userNode) ? userNode.GetString() : null;
            return new WebSocketInboundMessage(request ?? string.Empty, domain, userId);
        }
        catch (JsonException)
        {
            return new WebSocketInboundMessage(trimmed);
        }
    }

    public static string ToProcessorInput(WebSocketInboundMessage inbound, string? fallbackDomain)
    {
        var domain = string.IsNullOrWhiteSpace(inbound.Domain) ? fallbackDomain : inbound.Domain;
        if (string.IsNullOrWhiteSpace(domain))
            return inbound.Request;

        return $"[domain:{domain}] {inbound.Request}";
    }

    public static string BuildOutboundPayload(string response, WebSocketInboundMessage inbound, string? fallbackDomain)
    {
        var effectiveDomain = string.IsNullOrWhiteSpace(inbound.Domain) ? fallbackDomain : inbound.Domain;
        return JsonSerializer.Serialize(new
        {
            response,
            domain = effectiveDomain,
            userId = inbound.UserId,
            helper = "Send JSON payloads with {\"request\":\"...\",\"domain\":\"your-domain\",\"userId\":\"optional-user\"} for better deploy-time routing."
        });
    }

    public async Task RunAsync(Func<string, CancellationToken, Task<string>> onUserMessage, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(_listenerPrefix);
        listener.Start();
        Console.WriteLine("Vitruvian CLI started in WebSocket mode.");
        Console.WriteLine($"WebSocket listen prefix: {_listenerPrefix}");
        Console.WriteLine($"WebSocket connect URL: {_publicUrl}");
        Console.WriteLine("Developer helpers: include `domain` and `userId` in payload JSON for better routing and observability.");

        using var registration = cancellationToken.Register(() =>
        {
            if (listener.IsListening)
                listener.Stop();
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (context is null)
                    continue;

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    var error = Encoding.UTF8.GetBytes("Expected a WebSocket upgrade request.");
                    await context.Response.OutputStream.WriteAsync(error, cancellationToken);
                    context.Response.Close();
                    continue;
                }

                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                await HandleSocketAsync(wsContext.WebSocket, onUserMessage, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task HandleSocketAsync(WebSocket socket, Func<string, CancellationToken, Task<string>> onUserMessage, CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var payloadStream = new MemoryStream();
                WebSocketReceiveResult receiveResult;
                do
                {
                    receiveResult = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", cancellationToken);
                        return;
                    }

                    payloadStream.Write(buffer, 0, receiveResult.Count);
                } while (!receiveResult.EndOfMessage);

                var payload = Encoding.UTF8.GetString(payloadStream.ToArray());
                var inbound = ParseInbound(payload);
                var request = ToProcessorInput(inbound, defaultDomain);
                var response = await onUserMessage(request, cancellationToken);
                var outbound = BuildOutboundPayload(response, inbound, defaultDomain);
                var responseBytes = Encoding.UTF8.GetBytes(outbound);
                await socket.SendAsync(responseBytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
