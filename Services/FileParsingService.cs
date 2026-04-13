using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public interface IFileParsingService
{
    Task<DatasetModel> ParseAsync(IFormFile file, CancellationToken cancellationToken = default);
}

public sealed class FileParsingService : IFileParsingService
{
    public async Task<DatasetModel> ParseAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = file.OpenReadStream();

        return extension switch
        {
            ".csv" => await ParseCsvAsync(file.FileName, stream, cancellationToken),
            ".xlsx" => await ParseXlsxAsync(file.FileName, stream, cancellationToken),
            _ => throw new InvalidOperationException("Faqat CSV va XLSX formatlari qo'llab-quvvatlanadi.")
        };
    }

    private static async Task<DatasetModel> ParseCsvAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var rawLines = new List<string>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                rawLines.Add(line);
            }
        }

        var nonEmptyLines = rawLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (nonEmptyLines.Count == 0)
        {
            throw new InvalidOperationException("CSV fayl bo'sh.");
        }

        var delimiter = DetectDelimiter(nonEmptyLines[0]);
        var headers = NormalizeHeaders(SplitCsvLine(nonEmptyLines[0], delimiter));

        var rows = new List<Dictionary<string, string?>>();
        foreach (var line in nonEmptyLines.Skip(1))
        {
            var values = SplitCsvLine(line, delimiter);
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < headers.Count; i++)
            {
                row[headers[i]] = i < values.Count ? values[i] : null;
            }

            rows.Add(row);
        }

        return new DatasetModel
        {
            Id = Guid.NewGuid().ToString("N"),
            FileName = fileName,
            Columns = headers,
            Rows = rows
        };
    }

    private static Task<DatasetModel> ParseXlsxAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = ResolveFirstWorksheetPath(archive);
        var sheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException("XLSX ichida worksheet topilmadi.");

        using var sheetStream = sheetEntry.Open();
        var sheetDocument = XDocument.Load(sheetStream);
        var sheetNs = sheetDocument.Root?.Name.Namespace
            ?? throw new InvalidOperationException("Worksheet XML noto'g'ri.");

        var parsedRows = new SortedDictionary<int, Dictionary<int, string?>>();
        var rowNodes = sheetDocument
            .Descendants(sheetNs + "row")
            .ToList();

        foreach (var rowNode in rowNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowIndex = GetRowIndex(rowNode.Attribute("r")?.Value);
            var rowMap = new Dictionary<int, string?>();

            foreach (var cellNode in rowNode.Elements(sheetNs + "c"))
            {
                var reference = cellNode.Attribute("r")?.Value;
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                var columnIndex = CellReferenceToColumnIndex(reference);
                var cellType = cellNode.Attribute("t")?.Value;
                var value = ExtractCellValue(cellNode, cellType, sharedStrings, sheetNs);

                rowMap[columnIndex] = value;
            }

            parsedRows[rowIndex] = rowMap;
        }

        if (parsedRows.Count == 0)
        {
            throw new InvalidOperationException("XLSX ichida ma'lumot topilmadi.");
        }

        var headerRow = parsedRows.First().Value;
        var maxColumnIndex = parsedRows
            .SelectMany(row => row.Value.Keys)
            .DefaultIfEmpty(0)
            .Max();

        var rawHeaders = Enumerable.Range(0, maxColumnIndex + 1)
            .Select(index => headerRow.TryGetValue(index, out var value) ? value : null)
            .ToList();

        var headers = NormalizeHeaders(rawHeaders);

        var rows = new List<Dictionary<string, string?>>();
        foreach (var row in parsedRows.Skip(1))
        {
            var normalizedRow = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count; index++)
            {
                normalizedRow[headers[index]] = row.Value.TryGetValue(index, out var value) ? value : null;
            }

            var hasValue = normalizedRow.Values.Any(value => !string.IsNullOrWhiteSpace(value));
            if (hasValue)
            {
                rows.Add(normalizedRow);
            }
        }

        return Task.FromResult(new DatasetModel
        {
            Id = Guid.NewGuid().ToString("N"),
            FileName = fileName,
            Columns = headers,
            Rows = rows
        });
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        const string workbookPath = "xl/workbook.xml";
        const string relsPath = "xl/_rels/workbook.xml.rels";

        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        using var workbookStream = workbookEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var workbookNs = workbook.Root?.Name.Namespace ?? XNamespace.None;
        var relationshipNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        var firstSheet = workbook
            .Descendants(workbookNs + "sheet")
            .FirstOrDefault();

        var relationshipId = firstSheet?.Attribute(relationshipNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return "xl/worksheets/sheet1.xml";
        }

        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        using var relsStream = relsEntry.Open();
        var relsDocument = XDocument.Load(relsStream);
        var relsNs = relsDocument.Root?.Name.Namespace ?? XNamespace.None;
        var relationship = relsDocument
            .Descendants(relsNs + "Relationship")
            .FirstOrDefault(node => string.Equals(node.Attribute("Id")?.Value, relationshipId, StringComparison.OrdinalIgnoreCase));

        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            return "xl/worksheets/sheet1.xml";
        }

        var normalizedTarget = target.Replace('\\', '/').TrimStart('/');
        return normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalizedTarget
            : $"xl/{normalizedTarget}";
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        using var stream = sharedStringsEntry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        return document
            .Descendants(ns + "si")
            .Select(node =>
            {
                var richTextValues = node
                    .Descendants(ns + "t")
                    .Select(textNode => textNode.Value);

                return string.Concat(richTextValues);
            })
            .ToList();
    }

    private static string? ExtractCellValue(XElement cellNode, string? cellType, IReadOnlyList<string> sharedStrings, XNamespace sheetNs)
    {
        if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return cellNode
                .Element(sheetNs + "is")
                ?.Element(sheetNs + "t")
                ?.Value;
        }

        var rawValue = cellNode.Element(sheetNs + "v")?.Value;
        if (rawValue is null)
        {
            return null;
        }

        if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 &&
            index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        if (string.Equals(cellType, "b", StringComparison.OrdinalIgnoreCase))
        {
            return rawValue == "1" ? "true" : "false";
        }

        return rawValue;
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var currentChar = line[i];

            if (currentChar == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                insideQuotes = !insideQuotes;
                continue;
            }

            if (currentChar == delimiter && !insideQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(currentChar);
        }

        values.Add(current.ToString().Trim());
        return values;
    }

    private static char DetectDelimiter(string headerLine)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        return candidates
            .OrderByDescending(delimiter => headerLine.Count(character => character == delimiter))
            .First();
    }

    private static List<string> NormalizeHeaders(IEnumerable<string?> rawHeaders)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();
        var columnNumber = 1;

        foreach (var rawHeader in rawHeaders)
        {
            var baseName = string.IsNullOrWhiteSpace(rawHeader)
                ? $"Column{columnNumber}"
                : rawHeader.Trim();

            var uniqueName = baseName;
            var suffix = 2;

            while (!usedNames.Add(uniqueName))
            {
                uniqueName = $"{baseName}_{suffix}";
                suffix++;
            }

            normalized.Add(uniqueName);
            columnNumber++;
        }

        return normalized;
    }

    private static int GetRowIndex(string? rowReference)
    {
        if (string.IsNullOrWhiteSpace(rowReference))
        {
            return 0;
        }

        var digits = new string(rowReference.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
        {
            return 0;
        }

        return Math.Max(rowNumber - 1, 0);
    }

    private static int CellReferenceToColumnIndex(string cellReference)
    {
        var letters = new string(
            cellReference
                .TakeWhile(character => char.IsLetter(character))
                .ToArray()
        ).ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(letters))
        {
            return 0;
        }

        var columnNumber = 0;
        foreach (var letter in letters)
        {
            columnNumber = (columnNumber * 26) + (letter - 'A' + 1);
        }

        return Math.Max(columnNumber - 1, 0);
    }
}
