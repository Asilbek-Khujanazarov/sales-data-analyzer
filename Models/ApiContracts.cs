namespace SalesDataAnalyzer.Api.Models;

public sealed record UploadResponse(
    string DatasetId,
    string FileName,
    int RowCount,
    IReadOnlyList<string> Columns);

public sealed class PreviewResponse
{
    public required string DatasetId { get; init; }

    public required int TotalRows { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<Dictionary<string, string?>> Rows { get; init; }
}

public sealed class MetricCard
{
    public required string Label { get; init; }

    public required string Value { get; init; }

    public string? Hint { get; init; }
}

public sealed class MetricsResponse
{
    public required int RowCount { get; init; }

    public required IReadOnlyList<MetricCard> Cards { get; init; }
}

public sealed class ChartPoint
{
    public required string Label { get; init; }

    public required decimal Value { get; init; }
}

public sealed class ChartResponse
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<ChartPoint> Points { get; init; }

    public string? Note { get; init; }
}

public sealed class AskRequest
{
    public required string DatasetId { get; init; }

    public required string Question { get; init; }

    public string? SessionId { get; init; }

    public string? ApiKey { get; init; }
}

public sealed class AgentResponse
{
    public required string SessionId { get; init; }

    public required string Answer { get; init; }

    public required IReadOnlyList<string> SuggestedCharts { get; init; }

    public required IReadOnlyList<ChatMessageModel> History { get; init; }

    public string Source { get; init; } = "fallback";

    public string? DebugMessage { get; init; }
}
