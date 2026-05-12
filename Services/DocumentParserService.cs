using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TechNormBlazor.Data.Models;
using UglyToad.PdfPig;

namespace TechNormBlazor.Services;

public class ParsedRow
{
    public string CaseId       { get; set; } = "";
    public string Activity     { get; set; } = "";
    public string DateStr      { get; set; } = DateTime.Today.ToString("dd.MM.yyyy");
    public int    Shift        { get; set; } = 1;
    public int?   ProductId    { get; set; }
    public int?   ResourceId   { get; set; }
    public string ResourceName { get; set; } = "";
    public decimal DurationMin { get; set; }
    public int    Quantity     { get; set; } = 1;

    public DateTime ParsedDate =>
        DateTime.TryParseExact(DateStr, "dd.MM.yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : DateTime.UtcNow.Date;
}

public interface IDocumentParserService
{
    Task<List<ParsedRow>> ParseAsync(Document doc, IReadOnlyList<Resource> resources, IReadOnlyList<Product>? products = null);
}

public class DocumentParserService(ILogger<DocumentParserService> logger) : IDocumentParserService
{
    private static readonly Regex DateRe = new(@"^\d{2}\.\d{2}\.\d{4}$", RegexOptions.Compiled);

    public Task<List<ParsedRow>> ParseAsync(Document doc, IReadOnlyList<Resource> resources, IReadOnlyList<Product>? products = null)
    {
        if (!File.Exists(doc.FilePath))
        {
            logger.LogWarning("Parser: файл не найден {Path}", doc.FilePath);
            return Task.FromResult(new List<ParsedRow>());
        }

        return doc.FileType switch
        {
            "pdf"   => ParsePdfAsync(doc.FilePath, resources),
            "excel" => ParseExcelAsync(doc.FilePath, resources, products),
            _       => Task.FromResult(new List<ParsedRow>()),
        };
    }

    private Task<List<ParsedRow>> ParsePdfAsync(string filePath, IReadOnlyList<Resource> resources)
    {
        var result = new List<ParsedRow>();

        using var pdf = PdfDocument.Open(filePath);

        foreach (var page in pdf.GetPages())
        {
            var allWords = page.GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .OrderByDescending(w => w.BoundingBox.Bottom)   // top → bottom
                .ToList();

            if (allWords.Count == 0) continue;

            var lineWordGroups = new List<List<UglyToad.PdfPig.Content.Word>>();
            double lineMinY = double.MaxValue;

            foreach (var word in allWords)
            {
                var y = word.BoundingBox.Bottom;
                if (lineWordGroups.Count == 0 || lineMinY - y > 8)
                {
                    lineWordGroups.Add(new List<UglyToad.PdfPig.Content.Word> { word });
                    lineMinY = y;
                }
                else
                {
                    lineWordGroups[^1].Add(word);
                    lineMinY = Math.Min(lineMinY, y);
                }
            }

            var lines = lineWordGroups
                .Select(g => g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text).ToList())
                .ToList();

            logger.LogInformation("PDF parser: страница {N} — {L} строк, {W} слов",
                page.Number, lines.Count, allWords.Count);

            bool headerFound = false;
            foreach (var line in lines)
            {
                var lineStr = string.Join(" | ", line);

                if (!headerFound)
                {
                    if (line.Contains("№") ||
                        (line.Any(t => t.StartsWith("Операц", StringComparison.OrdinalIgnoreCase)) &&
                         line.Any(t => t.StartsWith("Дата",   StringComparison.OrdinalIgnoreCase))))
                    {
                        headerFound = true;
                        logger.LogInformation("PDF parser: найден заголовок → [{H}]", lineStr);
                    }
                    continue;
                }

                logger.LogInformation("PDF parser: строка → [{L}]", lineStr);

                if (line.Count < 5 || !int.TryParse(line[0], out _)) continue;

                var dateIdx = line.FindIndex(s => DateRe.IsMatch(s));
                if (dateIdx < 2)
                {
                    logger.LogInformation("PDF parser: дата не найдена — пропуск");
                    continue;
                }

                var caseId   = line[1];
                var activity = string.Join(" ", line.Skip(2).Take(dateIdx - 2));
                var date     = line[dateIdx];

                int.TryParse(line.ElementAtOrDefault(dateIdx + 1), out var shift);
                if (shift < 1 || shift > 3) shift = 1;

                var tail = line.Skip(dateIdx + 2).ToList();
                logger.LogInformation("PDF parser: tail → [{T}]", string.Join(" | ", tail));

                var numTokens = new List<string>();
                int resEnd = tail.Count;
                for (int ti = tail.Count - 1; ti >= 0; ti--)
                {
                    if (tail[ti].Any(char.IsLetter)) break;
                    numTokens.Insert(0, tail[ti]);
                    resEnd = ti;
                }

                logger.LogInformation("PDF parser: numTokens → [{N}]", string.Join(" | ", numTokens));

                if (numTokens.Count < 2)
                {
                    logger.LogWarning("PDF parser: мало числовых токенов ({C}) — пропуск", numTokens.Count);
                    continue;
                }

                if (!int.TryParse(numTokens[^1], out var qty)) continue;

                var durStr = string.Join("", numTokens.Take(numTokens.Count - 1))
                                   .Replace(',', '.').Trim('.');
                logger.LogInformation("PDF parser: durStr='{D}' qty={Q}", durStr, qty);

                if (!decimal.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
                {
                    logger.LogWarning("PDF parser: не удалось разобрать длительность '{D}'", durStr);
                    continue;
                }

                var resName = string.Join(" ", tail.Take(resEnd));

                result.Add(new ParsedRow
                {
                    CaseId       = caseId,
                    Activity     = activity,
                    DateStr      = date,
                    Shift        = shift,
                    ResourceId   = MatchResource(resName, resources)?.Id,
                    ResourceName = resName,
                    DurationMin  = dur,
                    Quantity     = qty > 0 ? qty : 1,
                });
            }
        }

        logger.LogInformation("PDF parser: итого распознано {Count} строк", result.Count);
        return Task.FromResult(result);
    }

    private static Task<List<ParsedRow>> ParseExcelAsync(
        string filePath, IReadOnlyList<Resource> resources, IReadOnlyList<Product>? products)
    {
        var result = new List<ParsedRow>();

        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 2) return Task.FromResult(result);

        int headerRow = 1;
        int caseCol = 0, actCol  = 0, dateCol  = 0, shiftCol = 0,
            prodCol = 0, resCol  = 0, durCol   = 0, qtyCol   = 0;

        for (int r = 1; r <= Math.Min(5, lastRow); r++)
        {
            for (int c = 1; c <= lastCol; c++)
            {
                var h = ws.Cell(r, c).GetString().ToLowerInvariant().Trim();
                if      (h.Contains("дело") || h.Contains("case") || h.StartsWith("id"))    { caseCol  = c; headerRow = r; }
                else if (h.Contains("операц") || h.Contains("активн"))                      { actCol   = c; }
                else if (h.Contains("дата")   || h.Contains("date"))                        { dateCol  = c; }
                else if (h.Contains("смен")   || h.Contains("shift"))                       { shiftCol = c; }
                else if (h.Contains("издел")  || h.Contains("product"))                     { prodCol  = c; }
                else if (h.Contains("ресурс") || h.Contains("resource"))                    { resCol   = c; }
                else if (h.Contains("длит")   || h.Contains("мин") || h.Contains("min"))   { durCol   = c; }
                else if (h.Contains("кол")    || h.Contains("qty"))                         { qtyCol   = c; }
            }
            if (caseCol > 0) break;
        }

        // Fallback: positional (1=№, 2=CaseId, 3=Op, 4=Date, 5=Shift, 6=Resource, 7=Dur, 8=Qty)
        if (caseCol == 0) { caseCol=2; actCol=3; dateCol=4; shiftCol=5; resCol=6; durCol=7; qtyCol=8; }

        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var caseId = ws.Cell(r, caseCol).GetString().Trim();
            if (string.IsNullOrEmpty(caseId)) continue;

            var activity = actCol  > 0 ? ws.Cell(r, actCol).GetString().Trim()  : "";
            var dateStr  = dateCol > 0 ? ws.Cell(r, dateCol).GetString().Trim() : "";
            var resName  = resCol  > 0 ? ws.Cell(r, resCol).GetString().Trim()  : "";
            var prodName = prodCol > 0 ? ws.Cell(r, prodCol).GetString().Trim() : "";

            int.TryParse(shiftCol > 0 ? ws.Cell(r, shiftCol).GetString() : "1", out var shift);
            decimal.TryParse(
                (durCol > 0 ? ws.Cell(r, durCol).GetString() : "0").Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var dur);
            int.TryParse(qtyCol > 0 ? ws.Cell(r, qtyCol).GetString() : "1", out var qty);

            result.Add(new ParsedRow
            {
                CaseId       = caseId,
                Activity     = activity,
                DateStr      = string.IsNullOrEmpty(dateStr) ? DateTime.Today.ToString("dd.MM.yyyy") : dateStr,
                Shift        = shift > 0 ? shift : 1,
                ProductId    = MatchProduct(prodName, products)?.Id,
                ResourceId   = MatchResource(resName, resources)?.Id,
                ResourceName = resName,
                DurationMin  = dur,
                Quantity     = qty > 0 ? qty : 1,
            });
        }

        return Task.FromResult(result);
    }

    private static Resource? MatchResource(string name, IReadOnlyList<Resource> list)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return list.FirstOrDefault(r =>
                   r.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(r.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Product? MatchProduct(string name, IReadOnlyList<Product>? list)
    {
        if (string.IsNullOrWhiteSpace(name) || list is null) return null;
        return list.FirstOrDefault(p =>
                   p.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
    }
}
