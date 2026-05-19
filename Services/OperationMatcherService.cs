using System.Text;
using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public enum MatchStatus { AutoMatched, NeedsConfirm, Unmatched }

public class MatchResult
{
    public MatchStatus Status         { get; set; }
    public Operation?  Operation      { get; set; }
    public decimal     Confidence     { get; set; }
    public bool        WasCached      { get; set; }
    public string      NormalizedInput { get; set; } = "";
}

public interface IOperationMatcherService
{
    Task<MatchResult>                  MatchAsync(string rawName);
    Task                               ConfirmMappingAsync(string rawName, int operationId, decimal confidence, int userId);
    Task<List<OperationNameMapping>>   GetAllMappingsAsync();
    Task                               DeleteMappingAsync(int id);
}

public class OperationMatcherService(IDbContextFactory<TechNormDbContext> factory) : IOperationMatcherService
{
    private const decimal AutoMatchThreshold = 0.85m;
    private const decimal SuggestThreshold   = 0.60m;

    public async Task<MatchResult> MatchAsync(string rawName)
    {
        var normalized = NormalizeString(rawName);

        await using var db = await factory.CreateDbContextAsync();

        // 1. Точное совпадение в кэше подтверждённых маппингов
        var cached = await db.OperationNameMappings
            .Include(m => m.Operation)
            .FirstOrDefaultAsync(m => m.RawName == rawName || m.NormalizedName == normalized);

        if (cached is not null)
            return new MatchResult
            {
                Status         = cached.IsConfirmed ? MatchStatus.AutoMatched : MatchStatus.NeedsConfirm,
                Operation      = cached.Operation,
                Confidence     = cached.Confidence,
                WasCached      = true,
                NormalizedInput = normalized,
            };

        // 2. Точное совпадение по имени в справочнике операций
        var operations = await db.Operations.ToListAsync();
        var exactMatch = operations.FirstOrDefault(o =>
            string.Equals(o.Name, rawName, StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
            return new MatchResult
            {
                Status         = MatchStatus.AutoMatched,
                Operation      = exactMatch,
                Confidence     = 1.0m,
                NormalizedInput = normalized,
            };

        // 3. Fuzzy matching по всем операциям справочника
        var bestScore = 0m;
        Operation? bestOp = null;

        foreach (var op in operations)
        {
            var score = ComputeScore(normalized, op.Name);
            if (score > bestScore)
            {
                bestScore = score;
                bestOp    = op;
            }
        }

        var confidence = Math.Round(bestScore, 4);

        if (confidence >= AutoMatchThreshold)
            return new MatchResult
            {
                Status         = MatchStatus.AutoMatched,
                Operation      = bestOp,
                Confidence     = confidence,
                NormalizedInput = normalized,
            };

        if (confidence >= SuggestThreshold)
            return new MatchResult
            {
                Status         = MatchStatus.NeedsConfirm,
                Operation      = bestOp,
                Confidence     = confidence,
                NormalizedInput = normalized,
            };

        return new MatchResult
        {
            Status         = MatchStatus.Unmatched,
            Confidence     = confidence,
            NormalizedInput = normalized,
        };
    }

    public async Task ConfirmMappingAsync(string rawName, int operationId, decimal confidence, int userId)
    {
        var normalized = NormalizeString(rawName);

        await using var db = await factory.CreateDbContextAsync();

        var existing = await db.OperationNameMappings
            .FirstOrDefaultAsync(m => m.RawName == rawName);

        if (existing is not null)
        {
            existing.OperationId    = operationId;
            existing.Confidence     = confidence;
            existing.IsConfirmed    = true;
            existing.ConfirmedBy    = userId;
            existing.ConfirmedAt    = DateTime.UtcNow;
            existing.NormalizedName = normalized;
        }
        else
        {
            db.OperationNameMappings.Add(new OperationNameMapping
            {
                RawName        = rawName,
                NormalizedName = normalized,
                OperationId    = operationId,
                Confidence     = confidence,
                IsConfirmed    = true,
                ConfirmedBy    = userId,
                ConfirmedAt    = DateTime.UtcNow,
                CreatedAt      = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<OperationNameMapping>> GetAllMappingsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.OperationNameMappings
            .Include(m => m.Operation)
            .Include(m => m.ConfirmedByUser)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task DeleteMappingAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.OperationNameMappings.Where(m => m.Id == id).ExecuteDeleteAsync();
    }

    // ── Алгоритмы ──────────────────────────────────────────────────────────

    // Объединённая оценка: max(Levenshtein, Jaccard) — учитывает и символьное, и токенное сходство
    private static decimal ComputeScore(string normalized, string candidate)
    {
        var normCandidate = NormalizeString(candidate);
        var lev           = LevenshteinSimilarity(normalized, normCandidate);
        var jac           = JaccardSimilarity(normalized, normCandidate);
        return Math.Max(lev, jac);
    }

    // Предобработка: нижний регистр, замена ё→е, удаление пунктуации, collapse пробелов
    internal static string NormalizeString(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var s = input.Trim().ToLowerInvariant().Replace('ё', 'е');

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');

        s = sb.ToString();

        // Collapse multiple spaces
        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    // Levenshtein similarity = 1 - distance / maxLength
    private static decimal LevenshteinSimilarity(string s, string t)
    {
        int maxLen = Math.Max(s.Length, t.Length);
        if (maxLen == 0) return 1m;
        return 1m - (decimal)LevenshteinDistance(s, t) / maxLen;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int m = s.Length, n = t.Length;
        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        return d[m, n];
    }

    // Jaccard similarity по токенам (словам): |A ∩ B| / |A ∪ B|
    private static decimal JaccardSimilarity(string s, string t)
    {
        var ts = new HashSet<string>(s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var tt = new HashSet<string>(t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (ts.Count == 0 && tt.Count == 0) return 1m;
        int intersection = ts.Intersect(tt).Count();
        int union        = ts.Union(tt).Count();
        return union == 0 ? 0m : (decimal)intersection / union;
    }
}
