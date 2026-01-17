using Dapper;
using DictionaryImporter.AITextKit.Grammar.Core;
using DictionaryImporter.AITextKit.Grammar.Core.Models;
using DictionaryImporter.AITextKit.Grammar.Core.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace DictionaryImporter.AITextKit.Grammar.Feature;

public sealed class GrammarFeature : IGrammarFeature, IGrammarCorrector
{
    private const int DbTimeoutSeconds = 180;

    private readonly string _connectionString;
    private readonly EnhancedGrammarConfiguration _settings;
    private readonly ILanguageDetector _languageDetector;
    private readonly IGrammarCorrector _corrector;
    private readonly IReadOnlyCollection<IGrammarEngine> _engines;
    private readonly ILogger<GrammarFeature> _logger;

    // ✅ Matches DI registration (6 args)
    public GrammarFeature(
        string connectionString,
        EnhancedGrammarConfiguration settings,
        ILanguageDetector languageDetector,
        IGrammarCorrector corrector,
        IEnumerable<IGrammarEngine> engines,
        ILogger<GrammarFeature> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _languageDetector = languageDetector ?? throw new ArgumentNullException(nameof(languageDetector));
        _corrector = corrector ?? throw new ArgumentNullException(nameof(corrector));
        _engines = engines?.ToArray() ?? Array.Empty<IGrammarEngine>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ✅ Pipeline entry point (REAL in codebase)
    public async Task CorrectSourceAsync(string sourceCode, CancellationToken ct)
    {
        if (!_settings.Enabled || !_settings.EnabledForSource(sourceCode))
        {
            _logger.LogInformation("Grammar correction disabled for source {Source}", sourceCode);
            return;
        }

        _logger.LogInformation("Grammar correction started | Source={Source}", sourceCode);

        await CorrectDefinitionsAsync(sourceCode, ct);
        await CorrectExamplesAsync(sourceCode, ct);

        _logger.LogInformation("Grammar correction completed | Source={Source}", sourceCode);
    }

    // ✅ Used by text service & parser
    public async Task<string> CleanAsync(
        string text,
        string? languageCode = null,
        bool applyAutoCorrection = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (!applyAutoCorrection)
            return text;

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            try
            {
                languageCode = _languageDetector.Detect(text) ?? _settings.DefaultLanguage;
            }
            catch
            {
                languageCode = _settings.DefaultLanguage;
            }
        }

        var result = await _corrector.AutoCorrectAsync(text, languageCode, ct);
        return string.IsNullOrWhiteSpace(result.CorrectedText) ? text : result.CorrectedText;
    }

    // ✅ Definition correction matches original tables
    private async Task CorrectDefinitionsAsync(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var records = await conn.QueryAsync<DefinitionRecord>(
            """
            SELECT p.DictionaryEntryParsedId,
                   p.Definition,
                   e.Word
            FROM dbo.DictionaryEntryParsed p
            JOIN dbo.DictionaryEntry e ON e.DictionaryEntryId = p.DictionaryEntryId
            WHERE e.SourceCode = @SourceCode
              AND LEN(p.Definition) > @MinLength
              AND (p.GrammarCorrected = 0 OR p.GrammarCorrected IS NULL)
            ORDER BY p.DictionaryEntryParsedId
            """,
            new { SourceCode = sourceCode, MinLength = _settings.MinDefinitionLength },
            commandTimeout: DbTimeoutSeconds);

        var total = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        var languageCode = _settings.GetLanguageCode(sourceCode);

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

                var result = await _corrector.AutoCorrectAsync(r.Definition, languageCode, ct);
                var finalText = string.IsNullOrWhiteSpace(result.CorrectedText) ? r.Definition : result.CorrectedText;

                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.DictionaryEntryParsed
                    SET Definition = @Definition,
                        GrammarCorrected = 1,
                        GrammarCorrectionDate = SYSUTCDATETIME()
                    WHERE DictionaryEntryParsedId = @Id
                    """,
                    new { Id = r.DictionaryEntryParsedId, Definition = finalText },
                    commandTimeout: DbTimeoutSeconds);

                updated++;

                if (updated % 500 == 0)
                {
                    _logger.LogInformation(
                        "Definition correction progress | Source={Source} | Updated={Updated} | TotalScanned={Total}",
                        sourceCode, updated, total);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Failed to correct definition for word '{Word}' (ID: {Id})",
                    r.Word, r.DictionaryEntryParsedId);
            }
        }

        _logger.LogInformation(
            "Definitions corrected | Source={Source} | Total={Total} | Updated={Updated} | Skipped={Skipped} | Failed={Failed}",
            sourceCode, total, updated, skipped, failed);
    }

    // ✅ Example correction matches original tables
    private async Task CorrectExamplesAsync(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var records = await conn.QueryAsync<ExampleRecord>(
            """
            SELECT ex.DictionaryEntryExampleId,
                   ex.ExampleText,
                   de.Word
            FROM dbo.DictionaryEntryExample ex
            JOIN dbo.DictionaryEntryParsed p ON p.DictionaryEntryParsedId = ex.DictionaryEntryParsedId
            JOIN dbo.DictionaryEntry de ON de.DictionaryEntryId = p.DictionaryEntryId
            WHERE de.SourceCode = @SourceCode
              AND LEN(ex.ExampleText) > 10
              AND (ex.GrammarCorrected = 0 OR ex.GrammarCorrected IS NULL)
            ORDER BY ex.DictionaryEntryExampleId
            """,
            new { SourceCode = sourceCode },
            commandTimeout: DbTimeoutSeconds);

        var total = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        var languageCode = _settings.GetLanguageCode(sourceCode);

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

                var result = await _corrector.AutoCorrectAsync(r.ExampleText, languageCode, ct);
                var finalText = string.IsNullOrWhiteSpace(result.CorrectedText) ? r.ExampleText : result.CorrectedText;

                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.DictionaryEntryExample
                    SET ExampleText = @ExampleText,
                        GrammarCorrected = 1,
                        GrammarCorrectionDate = SYSUTCDATETIME()
                    WHERE DictionaryEntryExampleId = @Id
                    """,
                    new { Id = r.DictionaryEntryExampleId, ExampleText = finalText },
                    commandTimeout: DbTimeoutSeconds);

                updated++;

                if (updated % 500 == 0)
                {
                    _logger.LogInformation(
                        "Example correction progress | Source={Source} | Updated={Updated} | TotalScanned={Total}",
                        sourceCode, updated, total);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Failed to correct example for word '{Word}' (ID: {Id})",
                    r.Word, r.DictionaryEntryExampleId);
            }
        }

        _logger.LogInformation(
            "Examples corrected | Source={Source} | Total={Total} | Updated={Updated} | Skipped={Skipped} | Failed={Failed}",
            sourceCode, total, updated, skipped, failed);
    }

    // ✅ IGrammarCorrector passthrough
    public Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
        => _corrector.CheckAsync(text, languageCode, ct);

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
        => _corrector.AutoCorrectAsync(text, languageCode, ct);

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
        => _corrector.SuggestImprovementsAsync(text, languageCode, ct);

    private sealed record DefinitionRecord(long DictionaryEntryParsedId, string Definition, string Word);

    private sealed record ExampleRecord(long DictionaryEntryExampleId, string ExampleText, string Word);
}