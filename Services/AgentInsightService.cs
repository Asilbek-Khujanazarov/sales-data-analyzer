using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public interface IAgentInsightService
{
    Task<AgentResponse> AskAsync(
        DatasetModel dataset,
        string sessionId,
        string question,
        IReadOnlyList<ChatMessageModel> history,
        string? apiKeyOverride = null,
        CancellationToken cancellationToken = default);
}

public sealed class AgentInsightService : IAgentInsightService
{
    private readonly IAnalyticsService _analyticsService;
    private readonly IOpenAiChatClient _openAiChatClient;
    private readonly IOptionsMonitor<OpenAiOptions> _options;

    public AgentInsightService(
        IAnalyticsService analyticsService,
        IOpenAiChatClient openAiChatClient,
        IOptionsMonitor<OpenAiOptions> options)
    {
        _analyticsService = analyticsService;
        _openAiChatClient = openAiChatClient;
        _options = options;
    }

    public async Task<AgentResponse> AskAsync(
        DatasetModel dataset,
        string sessionId,
        string question,
        IReadOnlyList<ChatMessageModel> history,
        string? apiKeyOverride = null,
        CancellationToken cancellationToken = default)
    {
        var fallback = _analyticsService.Ask(dataset, sessionId, question, history);
        var prompt = BuildSystemPrompt();
        var context = BuildDatasetContext(dataset);
        var allowFallback = _options.CurrentValue.AllowFallback;

        var aiResult = await _openAiChatClient.TryGenerateAsync(
            prompt,
            context,
            history,
            apiKeyOverride,
            cancellationToken);
        if (!aiResult.IsSuccess || string.IsNullOrWhiteSpace(aiResult.Content))
        {
            if (!allowFallback)
            {
                return new AgentResponse
                {
                    SessionId = sessionId,
                    Answer = "OpenAI javobi olinmadi. Iltimos, API key/model/network sozlamasini tekshiring.",
                    SuggestedCharts = [],
                    History = history,
                    Source = "error",
                    DebugMessage = aiResult.Error ?? "AI javobi olinmadi."
                };
            }

            return new AgentResponse
            {
                SessionId = fallback.SessionId,
                Answer = fallback.Answer,
                SuggestedCharts = fallback.SuggestedCharts,
                History = fallback.History,
                Source = "fallback",
                DebugMessage = aiResult.Error ?? "AI javobi olinmadi."
            };
        }

        return new AgentResponse
        {
            SessionId = sessionId,
            Answer = aiResult.Content.Trim(),
            SuggestedCharts = fallback.SuggestedCharts,
            History = history,
            Source = "openai",
            DebugMessage = null
        };
    }

    private string BuildDatasetContext(DatasetModel dataset)
    {
        var metrics = _analyticsService.BuildMetrics(dataset);
        var regionChart = _analyticsService.BuildChart(dataset, "region-sales");
        var productChart = _analyticsService.BuildChart(dataset, "product-sales");
        var trendChart = _analyticsService.BuildChart(dataset, "monthly-trend");

        var builder = new StringBuilder();
        builder.AppendLine($"File: {dataset.FileName}");
        builder.AppendLine($"Rows: {dataset.Rows.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Columns: {string.Join(", ", dataset.Columns)}");
        builder.AppendLine();

        builder.AppendLine("Metrics:");
        foreach (var metric in metrics.Cards.Take(8))
        {
            builder.AppendLine($"- {metric.Label}: {metric.Value}");
        }

        builder.AppendLine();
        builder.AppendLine("Top Region Chart Points:");
        foreach (var point in regionChart.Points.Take(8))
        {
            builder.AppendLine($"- {point.Label}: {point.Value.ToString("N2", CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine();
        builder.AppendLine("Top Product Chart Points:");
        foreach (var point in productChart.Points.Take(8))
        {
            builder.AppendLine($"- {point.Label}: {point.Value.ToString("N2", CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine();
        builder.AppendLine("Monthly Trend Points:");
        foreach (var point in trendChart.Points.TakeLast(8))
        {
            builder.AppendLine($"- {point.Label}: {point.Value.ToString("N2", CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine();
        builder.AppendLine("Sample Rows:");
        foreach (var row in dataset.Rows.Take(12))
        {
            var pairs = row.Select(item => $"{item.Key}={item.Value}");
            builder.AppendLine($"- {string.Join("; ", pairs)}");
        }

        return builder.ToString();
    }

    private static string BuildSystemPrompt()
    {
        return
            """
            You are an expert sales analyst assistant.
            Answer in Uzbek (Latin).
            Use dataset context and chat history memory.
            Keep answers practical, concise, and business-focused.
            If user asks for recommendations, provide 3-5 actionable points.
            If data is missing, say clearly what is missing.
            """;
    }
}
