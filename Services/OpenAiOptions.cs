namespace SalesDataAnalyzer.Api.Services;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public bool Enabled { get; init; } = true;

    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";

    public string Model { get; init; } = "gpt-4.1-mini";

    public int HistoryWindow { get; init; } = 12;

    public double Temperature { get; init; } = 0.2;

    public int MaxTokens { get; init; } = 700;

    public bool AllowFallback { get; init; } = false;
}
