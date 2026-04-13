using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public interface IOpenAiChatClient
{
    Task<OpenAiChatResult> TryGenerateAsync(
        string systemPrompt,
        string dataContext,
        IReadOnlyList<ChatMessageModel> history,
        string? apiKeyOverride = null,
        CancellationToken cancellationToken = default);
}

public sealed class OpenAiChatResult
{
    public bool IsSuccess { get; init; }

    public bool IsConfigured { get; init; }

    public string? Content { get; init; }

    public string? Error { get; init; }
}

public sealed class OpenAiChatClient : IOpenAiChatClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<OpenAiOptions> _options;
    private readonly ILogger<OpenAiChatClient> _logger;

    public OpenAiChatClient(HttpClient httpClient, IOptionsMonitor<OpenAiOptions> options, ILogger<OpenAiChatClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<OpenAiChatResult> TryGenerateAsync(
        string systemPrompt,
        string dataContext,
        IReadOnlyList<ChatMessageModel> history,
        string? apiKeyOverride = null,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var effectiveApiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? options.ApiKey
            : apiKeyOverride.Trim();

        if (!options.Enabled || string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            return new OpenAiChatResult
            {
                IsSuccess = false,
                IsConfigured = false,
                Error = "OpenAI API key topilmadi yoki OpenAI o'chirilgan."
            };
        }

        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = $"{systemPrompt}\n\nDATASET CONTEXT:\n{dataContext}"
            }
        };

        var historyWindow = Math.Clamp(options.HistoryWindow, 2, 50);
        foreach (var item in history.TakeLast(historyWindow))
        {
            if (!string.Equals(item.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            messages.Add(new
            {
                role = string.Equals(item.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                content = item.Content
            });
        }

        var payload = new
        {
            model = options.Model,
            temperature = options.Temperature,
            max_tokens = options.MaxTokens,
            messages
        };

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://api.openai.com/v1/"
            : options.BaseUrl;

        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveApiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI request failed. StatusCode: {StatusCode}. Body: {Body}", response.StatusCode, body);
                var readableError = ExtractApiErrorMessage(body);
                return new OpenAiChatResult
                {
                    IsSuccess = false,
                    IsConfigured = true,
                    Error = string.IsNullOrWhiteSpace(readableError)
                        ? $"OpenAI HTTP {(int)response.StatusCode} xatolik."
                        : $"OpenAI HTTP {(int)response.StatusCode}: {readableError}"
                };
            }

            using var document = JsonDocument.Parse(body);
            var content = ExtractMessageContent(document.RootElement);
            if (string.IsNullOrWhiteSpace(content))
            {
                return new OpenAiChatResult
                {
                    IsSuccess = false,
                    IsConfigured = true,
                    Error = "OpenAI bo'sh javob qaytardi."
                };
            }

            return new OpenAiChatResult
            {
                IsSuccess = true,
                IsConfigured = true,
                Content = content
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "OpenAI request failed with exception.");
            return new OpenAiChatResult
            {
                IsSuccess = false,
                IsConfigured = true,
                Error = "OpenAI so'rovida istisno yuz berdi."
            };
        }
    }

    private static string? ExtractMessageContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var joined = content.EnumerateArray()
                .Select(item => item.TryGetProperty("text", out var textNode) ? textNode.GetString() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!);

            return string.Join("\n", joined);
        }

        return null;
    }

    private static string? ExtractApiErrorMessage(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("error", out var errorNode))
            {
                return null;
            }

            if (errorNode.ValueKind == JsonValueKind.String)
            {
                return errorNode.GetString();
            }

            if (errorNode.ValueKind == JsonValueKind.Object &&
                errorNode.TryGetProperty("message", out var messageNode) &&
                messageNode.ValueKind == JsonValueKind.String)
            {
                return messageNode.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
