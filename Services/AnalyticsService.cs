using System.Globalization;
using System.Text;
using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public interface IAnalyticsService
{
    MetricsResponse BuildMetrics(DatasetModel dataset);

    ChartResponse BuildChart(DatasetModel dataset, string type);

    AgentResponse Ask(DatasetModel dataset, string sessionId, string question, IReadOnlyList<ChatMessageModel> history);
}

public sealed class AnalyticsService : IAnalyticsService
{
    private static readonly string[] SalesColumnHints = ["sales", "amount", "revenue", "total", "summa"];
    private static readonly string[] ProductColumnHints = ["product", "item", "sku", "name", "mahsulot"];
    private static readonly string[] RegionColumnHints = ["region", "country", "city", "area", "state", "hudud"];
    private static readonly string[] DateColumnHints = ["date", "month", "time", "created", "sana"];

    public MetricsResponse BuildMetrics(DatasetModel dataset)
    {
        var cards = new List<MetricCard>
        {
            new() { Label = "Rows", Value = dataset.Rows.Count.ToString(CultureInfo.InvariantCulture), Hint = "Yuklangan yozuvlar soni" }
        };

        var salesColumn = FindBestNumericColumn(dataset, SalesColumnHints);
        if (salesColumn is not null)
        {
            var salesValues = dataset.Rows
                .Select(row => row.TryGetValue(salesColumn, out var value) ? value : null)
                .Select(ParseDecimal)
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .ToList();

            if (salesValues.Count > 0)
            {
                var total = salesValues.Sum();
                var average = salesValues.Average();

                cards.Add(new MetricCard
                {
                    Label = "Total Sales",
                    Value = total.ToString("N2", CultureInfo.InvariantCulture),
                    Hint = $"Column: {salesColumn}"
                });

                cards.Add(new MetricCard
                {
                    Label = "Avg Sales",
                    Value = average.ToString("N2", CultureInfo.InvariantCulture),
                    Hint = "O'rtacha savdo miqdori"
                });
            }
        }

        var productColumn = FindColumn(dataset, ProductColumnHints);
        if (productColumn is not null)
        {
            var topProduct = GroupByText(dataset, productColumn, salesColumn)
                .OrderByDescending(item => item.Value)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(topProduct.Key))
            {
                cards.Add(new MetricCard
                {
                    Label = "Top Product",
                    Value = topProduct.Key,
                    Hint = topProduct.Value.ToString("N2", CultureInfo.InvariantCulture)
                });
            }
        }

        var regionColumn = FindColumn(dataset, RegionColumnHints);
        if (regionColumn is not null)
        {
            var topRegion = GroupByText(dataset, regionColumn, salesColumn)
                .OrderByDescending(item => item.Value)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(topRegion.Key))
            {
                cards.Add(new MetricCard
                {
                    Label = "Top Region",
                    Value = topRegion.Key,
                    Hint = topRegion.Value.ToString("N2", CultureInfo.InvariantCulture)
                });
            }
        }

        return new MetricsResponse
        {
            RowCount = dataset.Rows.Count,
            Cards = cards
        };
    }

    public ChartResponse BuildChart(DatasetModel dataset, string type)
    {
        var chartType = (type ?? string.Empty).Trim().ToLowerInvariant();
        var salesColumn = FindBestNumericColumn(dataset, SalesColumnHints);

        return chartType switch
        {
            "product-sales" => BuildCategoricalChart(dataset, "product-sales", "Top Product Sales", FindColumn(dataset, ProductColumnHints), salesColumn),
            "monthly-trend" => BuildMonthlyTrendChart(dataset, salesColumn),
            _ => BuildCategoricalChart(dataset, "region-sales", "Regional Sales", FindColumn(dataset, RegionColumnHints), salesColumn)
        };
    }

    public AgentResponse Ask(DatasetModel dataset, string sessionId, string question, IReadOnlyList<ChatMessageModel> history)
    {
        var prompt = (question ?? string.Empty).Trim();
        if (prompt.Length == 0)
        {
            return new AgentResponse
            {
                SessionId = sessionId,
                Answer = "Savol bo'sh. Iltimos, aniq savol yozing.",
                SuggestedCharts = ["region-sales"],
                History = history
            };
        }

        var lowered = prompt.ToLowerInvariant();
        var metrics = BuildMetrics(dataset);
        var salesColumn = FindBestNumericColumn(dataset, SalesColumnHints);
        var productColumn = FindColumn(dataset, ProductColumnHints);
        var regionColumn = FindColumn(dataset, RegionColumnHints);
        var dateColumn = FindColumn(dataset, DateColumnHints);
        var asksCount = ContainsAny(lowered, ["nechta", "nechta?", "nechita", "nichta", "nichita", "qancha", "count", "how many"]);
        var asksDistinct = ContainsAny(lowered, ["unique", "distinct", "unikal", "turli"]);
        var asksProduct = ContainsAny(lowered, ["product", "products", "mahsulot", "item", "sku"]);
        var asksRegion = ContainsAny(lowered, ["region", "hudud", "state", "city", "country"]);
        var suggestedCharts = new List<string>();
        var answer = new StringBuilder();

        answer.AppendLine($"Dataset: {dataset.FileName}");
        answer.AppendLine($"Qatorlar soni: {dataset.Rows.Count}");

        if (asksCount && asksProduct)
        {
            if (productColumn is null)
            {
                answer.AppendLine("`Product` ustuni topilmadi.");
            }
            else
            {
                var allProducts = dataset.Rows
                    .Select(row => row.TryGetValue(productColumn, out var value) ? value : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim())
                    .ToList();

                var distinctProducts = allProducts
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                answer.AppendLine($"Product yozuvlari soni: {allProducts.Count}");
                answer.AppendLine($"Unikal productlar soni: {distinctProducts.Count}");

                if (!asksDistinct)
                {
                    var sample = distinctProducts.Take(8).ToList();
                    if (sample.Count > 0)
                    {
                        answer.AppendLine($"Misol productlar: {string.Join(", ", sample)}");
                    }
                }
            }

            suggestedCharts.Add("product-sales");
        }
        else if (asksCount && asksRegion)
        {
            if (regionColumn is null)
            {
                answer.AppendLine("`Region` ustuni topilmadi.");
            }
            else
            {
                var allRegions = dataset.Rows
                    .Select(row => row.TryGetValue(regionColumn, out var value) ? value : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim())
                    .ToList();

                var distinctRegions = allRegions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                answer.AppendLine($"Region yozuvlari soni: {allRegions.Count}");
                answer.AppendLine($"Unikal regionlar soni: {distinctRegions.Count}");
                if (distinctRegions.Count > 0)
                {
                    answer.AppendLine($"Regionlar: {string.Join(", ", distinctRegions.Take(10))}");
                }
            }

            suggestedCharts.Add("region-sales");
        }
        else if (asksCount)
        {
            answer.AppendLine($"Jami yozuvlar soni: {dataset.Rows.Count}");
            answer.AppendLine($"Ustunlar soni: {dataset.Columns.Count}");
        }
        else if ((lowered.Contains("top") && lowered.Contains("product")) || lowered.Contains("mahsulot"))
        {
            if (productColumn is null)
            {
                answer.AppendLine("`Product` ustuni topilmadi.");
            }
            else
            {
                var topProduct = GroupByText(dataset, productColumn, salesColumn)
                    .OrderByDescending(item => item.Value)
                    .Take(3)
                    .ToList();

                answer.AppendLine("Top productlar:");
                foreach (var item in topProduct)
                {
                    answer.AppendLine($"- {item.Key}: {item.Value:N2}");
                }

                suggestedCharts.Add("product-sales");
            }
        }
        else if (lowered.Contains("region") || lowered.Contains("hudud"))
        {
            if (regionColumn is null)
            {
                answer.AppendLine("`Region` tipidagi ustun topilmadi.");
            }
            else
            {
                var regions = GroupByText(dataset, regionColumn, salesColumn)
                    .OrderByDescending(item => item.Value)
                    .Take(5)
                    .ToList();

                answer.AppendLine("Region kesimidagi natijalar:");
                foreach (var item in regions)
                {
                    answer.AppendLine($"- {item.Key}: {item.Value:N2}");
                }

                suggestedCharts.Add("region-sales");
            }
        }
        else if (lowered.Contains("month") || lowered.Contains("oy") || lowered.Contains("trend"))
        {
            if (dateColumn is null)
            {
                answer.AppendLine("Sana/oy ustuni topilmadi, shuning uchun trend qurilmadi.");
            }
            else
            {
                var trend = BuildMonthlyTrend(dataset, salesColumn, dateColumn)
                    .TakeLast(6)
                    .ToList();

                answer.AppendLine("Oxirgi oylar bo'yicha trend:");
                foreach (var point in trend)
                {
                    answer.AppendLine($"- {point.Label}: {point.Value:N2}");
                }

                suggestedCharts.Add("monthly-trend");
            }
        }
        else
        {
            foreach (var card in metrics.Cards.Take(5))
            {
                answer.AppendLine($"- {card.Label}: {card.Value}");
            }

            answer.AppendLine("So'rovlar uchun misollar:");
            answer.AppendLine("- Top productlarni ko'rsat");
            answer.AppendLine("- Region bo'yicha savdo");
            answer.AppendLine("- Oylik trendni chiqar");
            suggestedCharts.AddRange(["region-sales", "product-sales"]);
        }

        if (salesColumn is null)
        {
            answer.AppendLine("Izoh: aniq `sales/amount` ustuni topilmadi, ayrim javoblar count asosida hisoblandi.");
        }

        return new AgentResponse
        {
            SessionId = sessionId,
            Answer = answer.ToString().Trim(),
            SuggestedCharts = suggestedCharts.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            History = history
        };
    }

    private static bool ContainsAny(string source, IEnumerable<string> terms)
    {
        foreach (var term in terms)
        {
            if (source.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ChartResponse BuildCategoricalChart(DatasetModel dataset, string type, string title, string? categoryColumn, string? numericColumn)
    {
        if (categoryColumn is null)
        {
            return new ChartResponse
            {
                Type = type,
                Title = title,
                Points = [],
                Note = "Kerakli category ustuni topilmadi."
            };
        }

        var points = GroupByText(dataset, categoryColumn, numericColumn)
            .OrderByDescending(item => item.Value)
            .Take(8)
            .Select(item => new ChartPoint
            {
                Label = item.Key,
                Value = item.Value
            })
            .ToList();

        return new ChartResponse
        {
            Type = type,
            Title = title,
            Points = points,
            Note = points.Count == 0 ? "Chart uchun ma'lumot yo'q." : null
        };
    }

    private static ChartResponse BuildMonthlyTrendChart(DatasetModel dataset, string? numericColumn)
    {
        var dateColumn = FindColumn(dataset, DateColumnHints);
        if (dateColumn is null)
        {
            return new ChartResponse
            {
                Type = "monthly-trend",
                Title = "Monthly Trend",
                Points = [],
                Note = "Sana ustuni topilmadi."
            };
        }

        var points = BuildMonthlyTrend(dataset, numericColumn, dateColumn).ToList();
        return new ChartResponse
        {
            Type = "monthly-trend",
            Title = "Monthly Trend",
            Points = points,
            Note = points.Count == 0 ? "Trend uchun yetarli ma'lumot yo'q." : null
        };
    }

    private static IReadOnlyList<ChartPoint> BuildMonthlyTrend(DatasetModel dataset, string? numericColumn, string dateColumn)
    {
        var grouped = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataset.Rows)
        {
            if (!row.TryGetValue(dateColumn, out var dateValue) ||
                !DateTime.TryParse(dateValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            {
                continue;
            }

            var bucket = new DateTime(date.Year, date.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var amount = 1m;

            if (numericColumn is not null &&
                row.TryGetValue(numericColumn, out var numericRaw) &&
                ParseDecimal(numericRaw).HasValue)
            {
                amount = ParseDecimal(numericRaw)!.Value;
            }

            grouped[bucket] = grouped.TryGetValue(bucket, out var current) ? current + amount : amount;
        }

        return grouped
            .OrderBy(item => item.Key)
            .Select(item => new ChartPoint { Label = item.Key, Value = item.Value })
            .ToList();
    }

    private static Dictionary<string, decimal> GroupByText(DatasetModel dataset, string categoryColumn, string? numericColumn)
    {
        var grouped = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataset.Rows)
        {
            if (!row.TryGetValue(categoryColumn, out var rawCategory) || string.IsNullOrWhiteSpace(rawCategory))
            {
                continue;
            }

            var category = rawCategory.Trim();
            var increment = 1m;

            if (numericColumn is not null &&
                row.TryGetValue(numericColumn, out var numericRaw) &&
                ParseDecimal(numericRaw).HasValue)
            {
                increment = ParseDecimal(numericRaw)!.Value;
            }

            grouped[category] = grouped.TryGetValue(category, out var current) ? current + increment : increment;
        }

        return grouped;
    }

    private static string? FindColumn(DatasetModel dataset, IEnumerable<string> hints)
    {
        foreach (var hint in hints)
        {
            var column = dataset.Columns.FirstOrDefault(name => name.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (column is not null)
            {
                return column;
            }
        }

        return null;
    }

    private static string? FindBestNumericColumn(DatasetModel dataset, IEnumerable<string> prioritizedHints)
    {
        var hintedColumn = FindColumn(dataset, prioritizedHints);
        if (hintedColumn is not null)
        {
            return hintedColumn;
        }

        var candidates = dataset.Columns
            .Select(column => new
            {
                Name = column,
                NumericCount = dataset.Rows
                    .Select(row => row.TryGetValue(column, out var value) ? value : null)
                    .Count(value => ParseDecimal(value).HasValue)
            })
            .Where(candidate => candidate.NumericCount > 0)
            .OrderByDescending(candidate => candidate.NumericCount)
            .ToList();

        return candidates.FirstOrDefault()?.Name;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var cleaned = new string(trimmed
            .Where(character => char.IsDigit(character) || character is '.' or ',' or '-')
            .ToArray());

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (cleaned.Count(character => character == ',') > 0 && cleaned.Count(character => character == '.') > 0)
        {
            cleaned = cleaned.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (cleaned.Count(character => character == ',') > 0)
        {
            cleaned = cleaned.Replace(',', '.');
        }

        if (decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return null;
    }
}
