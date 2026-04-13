using System.Text.RegularExpressions;
using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public interface ISalesDomainValidationService
{
    DomainValidationResult ValidateDataset(DatasetModel dataset);

    DomainValidationResult ValidateQuestion(DatasetModel dataset, string question);
}

public sealed class DomainValidationResult
{
    public bool IsValid { get; init; }

    public string? Message { get; init; }

    public static DomainValidationResult Success() => new() { IsValid = true };

    public static DomainValidationResult Fail(string message) => new() { IsValid = false, Message = message };
}

public sealed class SalesDomainValidationService : ISalesDomainValidationService
{
    private static readonly string[] SalesTerms =
    [
        "sales", "sale", "savdo", "sotuv", "revenue", "daromad", "profit", "foyda",
        "order", "buyurtma", "customer", "mijoz", "product", "mahsulot", "region", "hudud",
        "quantity", "miqdor", "count", "trend", "month", "oy", "kpi", "total", "sum", "avg", "average",
        "price", "narx", "discount", "segment", "growth", "forecast"
    ];

    private static readonly string[] OffTopicTerms =
    [
        "futbol", "basketbol", "kino", "movie", "music", "musiqa",
        "recipe", "retsept", "travel", "sayohat", "politics", "siyosat"
    ];

    public DomainValidationResult ValidateDataset(DatasetModel dataset)
    {
        if (dataset.Rows.Count == 0)
        {
            return DomainValidationResult.Fail("Dataset bo'sh. Sales tahlil uchun kamida bitta qator kerak.");
        }

        if (dataset.Columns.Count < 2)
        {
            return DomainValidationResult.Fail(
                "Dataset ustunlari juda kam. Kamida 2 ta ustun bo'lishi kerak.");
        }

        return DomainValidationResult.Success();
    }

    public DomainValidationResult ValidateQuestion(DatasetModel dataset, string question)
    {
        var cleaned = (question ?? string.Empty).Trim();
        if (cleaned.Length == 0)
        {
            return DomainValidationResult.Fail("Savol yuborilmadi.");
        }

        if (cleaned.Length > 700)
        {
            return DomainValidationResult.Fail("Savol juda uzun. 700 belgidan qisqaroq yozing.");
        }

        var normalizedQuestion = Normalize(cleaned);
        var hasSalesTerm = SalesTerms.Any(term => normalizedQuestion.Contains(term, StringComparison.OrdinalIgnoreCase));
        var hasColumnReference = HasColumnReference(dataset.Columns, normalizedQuestion);
        var hasOffTopicTerm = OffTopicTerms.Any(term => normalizedQuestion.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (hasOffTopicTerm && !hasSalesTerm && !hasColumnReference)
        {
            return DomainValidationResult.Fail(
                "Savol sales mavzusidan uzoq ko'rindi. Iltimos, savdo dataseti bo'yicha savol bering.");
        }

        return DomainValidationResult.Success();
    }

    private static bool HasColumnReference(IEnumerable<string> columns, string normalizedQuestion)
    {
        foreach (var column in columns)
        {
            var normalizedColumn = Normalize(column);
            if (normalizedColumn.Length < 3)
            {
                continue;
            }

            if (normalizedQuestion.Contains(normalizedColumn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parts = Regex.Split(normalizedColumn, @"[^a-z0-9]+")
                .Where(part => part.Length >= 4);

            foreach (var part in parts)
            {
                if (normalizedQuestion.Contains(part, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
