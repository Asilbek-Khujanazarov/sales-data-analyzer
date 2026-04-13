namespace SalesDataAnalyzer.Api.Models;

public sealed class DatasetModel
{
    public required string Id { get; init; }

    public required string FileName { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<Dictionary<string, string?>> Rows { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ChatMessageModel
{
    public required string Role { get; init; }

    public required string Content { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
