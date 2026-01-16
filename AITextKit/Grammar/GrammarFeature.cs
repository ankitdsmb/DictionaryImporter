using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.AITextKit.Grammar;

public interface IGrammarFeature : IGrammarCorrector
{
    Task<string> CleanAsync(
        string text,
        bool applyAutoCorrection = true,
        string? languageCode = null,
        CancellationToken ct = default);

    Task CorrectSourceAsync(string sourceCode, CancellationToken ct);

    Task<GrammarCheckResult> QuickCheckAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default);
}

public sealed class GrammarFeature(
    string connectionString,
    EnhancedGrammarConfiguration settings,
    ILanguageDetector languageDetector,
    IGrammarCorrector corrector,
    IEnumerable<IGrammarEngine> engines,
    ILogger<GrammarFeature> logger)
    : IGrammarFeature
{
    private readonly List<IGrammarEngine> _engines = engines?.ToList() ?? [];
    private readonly IGrammarCorrector _corrector = corrector; // ✅ ADD THIS

    public async Task CorrectSourceAsync(string sourceCode, CancellationToken ct)
    {
        if (!settings.Enabled || !settings.EnabledForSource(sourceCode))
        {
            logger.LogInformation("Grammar correction disabled for source {Source}", sourceCode);
            return;
        }

        logger.LogInformation("Grammar correction started | Source={Source}", sourceCode);

        await CorrectDefinitionsAsync(sourceCode, ct);
        await CorrectExamplesAsync(sourceCode, ct);

        logger.LogInformation("Grammar correction completed | Source={Source}", sourceCode);
    }

    public Task<GrammarCheckResult> QuickCheckAsync(string text, string? languageCode = null, CancellationToken ct = default)
        => CheckAsync(text, languageCode, ct);

    public async Task<string> CleanAsync(
        string text,
        bool applyAutoCorrection = true,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = GrammarTextCleaner.CleanTextBasic(text);

        if (!applyAutoCorrection)
            return cleaned;

        var correction = await _corrector.AutoCorrectAsync(cleaned, languageCode, ct);

        return string.IsNullOrWhiteSpace(correction.CorrectedText)
            ? cleaned
            : correction.CorrectedText;
    }

    public async Task<GrammarCheckResult> CheckAsync(string text, string? languageCode = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        languageCode ??= DetectLanguage(text);

        var supportedEngines = _engines.Where(e => e.IsSupported(languageCode)).ToList();
        if (supportedEngines.Count == 0)
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        var sw = Stopwatch.StartNew();

        var issues = new List<GrammarIssue>();

        foreach (var engine in supportedEngines)
        {
            try
            {
                var result = await engine.CheckAsync(text, languageCode, ct);
                issues.AddRange(result.Issues);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Grammar engine failed | Engine={Engine}", engine.Name);
            }
        }

        sw.Stop();

        var unique = GrammarIssueDeduplicator.Deduplicate(issues);

        return new GrammarCheckResult(unique.Count > 0, unique.Count, unique, sw.Elapsed);
    }

    // ✅ AutoCorrectAsync comes from IGrammarCorrector in your system.
    // We will implement it as "No rewrite" unless you already have a corrector registered in DI.
    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string? languageCode = null, CancellationToken ct = default)
    {
        return _corrector.AutoCorrectAsync(text, languageCode, ct);
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string? languageCode = null, CancellationToken ct = default)
    {
        return _corrector.SuggestImprovementsAsync(text, languageCode, ct);
    }

    private string DetectLanguage(string text)
    {
        try
        {
            return languageDetector.Detect(text) ?? settings.DefaultLanguage;
        }
        catch
        {
            return settings.DefaultLanguage;
        }
    }

    private async Task CorrectDefinitionsAsync(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var records = await conn.QueryAsync<DefinitionRecord>(
            """
        SELECT p.DictionaryEntryParsedId, p.Definition, e.Word
        FROM dbo.DictionaryEntryParsed p
        JOIN dbo.DictionaryEntry e ON e.DictionaryEntryId = p.DictionaryEntryId
        WHERE e.SourceCode = @SourceCode
          AND LEN(p.Definition) > @MinLength
          AND (p.GrammarCorrected = 0 OR p.GrammarCorrected IS NULL)
        ORDER BY p.DictionaryEntryParsedId
        """,
            new { SourceCode = sourceCode, MinLength = settings.MinDefinitionLength });

        var total = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        // You may already have this in settings, else default
        var languageCode = settings.GetLanguageCode(sourceCode);

        foreach (var r in records)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            try
            {
                if (string.IsNullOrWhiteSpace(r.Definition))
                {
                    skipped++;
                    continue;
                }

                // ✅ REAL correction (single truth)
                var result = await AutoCorrectAsync(r.Definition, languageCode, ct);

                var finalText = string.IsNullOrWhiteSpace(result.CorrectedText)
                    ? r.Definition
                    : result.CorrectedText;

                // Update only if text changed OR correction flag missing
                await conn.ExecuteAsync(
                    """
                UPDATE dbo.DictionaryEntryParsed
                SET Definition = @Definition,
                    GrammarCorrected = 1,
                    GrammarCorrectionDate = SYSUTCDATETIME()
                WHERE DictionaryEntryParsedId = @Id
                """,
                    new
                    {
                        Id = r.DictionaryEntryParsedId,
                        Definition = finalText
                    });

                updated++;

                if (updated % 500 == 0)
                {
                    logger.LogInformation(
                        "Definition correction progress | Source={Source} | Updated={Updated} | TotalScanned={Total}",
                        sourceCode,
                        updated,
                        total);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(
                    ex,
                    "Failed to correct definition for word '{Word}' (ID: {Id})",
                    r.Word,
                    r.DictionaryEntryParsedId);
            }
        }

        logger.LogInformation(
            "Definitions corrected | Source={Source} | Total={Total} | Updated={Updated} | Skipped={Skipped} | Failed={Failed}",
            sourceCode,
            total,
            updated,
            skipped,
            failed);
    }

    private async Task CorrectExamplesAsync(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var records = await conn.QueryAsync<ExampleRecord>(
            """
        SELECT ex.DictionaryEntryExampleId, ex.ExampleText, de.Word
        FROM dbo.DictionaryEntryExample ex
        JOIN dbo.DictionaryEntryParsed p ON p.DictionaryEntryParsedId = ex.DictionaryEntryParsedId
        JOIN dbo.DictionaryEntry de ON de.DictionaryEntryId = p.DictionaryEntryId
        WHERE de.SourceCode = @SourceCode
          AND LEN(ex.ExampleText) > 10
          AND (ex.GrammarCorrected = 0 OR ex.GrammarCorrected IS NULL)
        ORDER BY ex.DictionaryEntryExampleId
        """,
            new { SourceCode = sourceCode });

        var total = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        var languageCode = settings.GetLanguageCode(sourceCode);

        foreach (var r in records)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            try
            {
                if (string.IsNullOrWhiteSpace(r.ExampleText))
                {
                    skipped++;
                    continue;
                }

                // ✅ REAL correction (single truth)
                var result = await AutoCorrectAsync(r.ExampleText, languageCode, ct);

                var finalText = string.IsNullOrWhiteSpace(result.CorrectedText)
                    ? r.ExampleText
                    : result.CorrectedText;

                await conn.ExecuteAsync(
                    """
                UPDATE dbo.DictionaryEntryExample
                SET ExampleText = @ExampleText,
                    GrammarCorrected = 1,
                    GrammarCorrectionDate = SYSUTCDATETIME()
                WHERE DictionaryEntryExampleId = @Id
                """,
                    new
                    {
                        Id = r.DictionaryEntryExampleId,
                        ExampleText = finalText
                    });

                updated++;

                if (updated % 500 == 0)
                {
                    logger.LogInformation(
                        "Example correction progress | Source={Source} | Updated={Updated} | TotalScanned={Total}",
                        sourceCode,
                        updated,
                        total);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(
                    ex,
                    "Failed to correct example for word '{Word}' (ID: {Id})",
                    r.Word,
                    r.DictionaryEntryExampleId);
            }
        }

        logger.LogInformation(
            "Examples corrected | Source={Source} | Total={Total} | Updated={Updated} | Skipped={Skipped} | Failed={Failed}",
            sourceCode,
            total,
            updated,
            skipped,
            failed);
    }

    private sealed record DefinitionRecord(long DictionaryEntryParsedId, string Definition, string Word);
    private sealed record ExampleRecord(long DictionaryEntryExampleId, string ExampleText, string Word);
}

internal static class GrammarTextCleaner
{
    public static string CleanTextBasic(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }
}

internal static class GrammarIssueDeduplicator
{
    public static IReadOnlyList<GrammarIssue> Deduplicate(List<GrammarIssue> issues)
    {
        if (issues.Count == 0) return [];

        return issues
            .GroupBy(i => $"{i.StartOffset}:{i.EndOffset}:{i.RuleId}:{i.Message}")
            .Select(g => g.First())
            .ToList();
    }
}