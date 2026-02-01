using DictionaryImporter.Gateway.Grammar.Configuration;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;

namespace DictionaryImporter.Gateway.Grammar.Feature;

public sealed class GrammarFeature(
    string connectionString,
    EnhancedGrammarConfiguration settings,
    INTextCatLangDetector languageDetector,
    IGrammarCorrector corrector,
    IEnumerable<IGrammarEngine> engines,
    ILogger<GrammarFeature> logger)
    : IGrammarFeature, IGrammarCorrector
{
    private const int DbTimeoutSeconds = 180;
    private const int BatchSize = 2000;
    private const string SpName = "dbo.sp_Grammar_Process";
    private const int MaxParallelism = 4;
    private const int ClaimBatchSize = 500;
    private static readonly Guid WorkerId = Guid.NewGuid();

    private readonly string _connectionString = connectionString;
    private readonly EnhancedGrammarConfiguration _settings = settings;
    private readonly INTextCatLangDetector _languageDetector = languageDetector;
    private readonly IGrammarCorrector _corrector = corrector;
    private readonly ILogger<GrammarFeature> _logger = logger;

    public async Task CorrectSourceAsync(string sourceCode, CancellationToken ct)
    {
        if (!_settings.Enabled || !_settings.EnabledForSource(sourceCode))
        {
            _logger.LogInformation("Grammar correction disabled | Source={Source}", sourceCode);
            return;
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var languageCode = _settings.GetLanguageCode(sourceCode);

        await CorrectDefinitionsAsync(conn, sourceCode, languageCode, ct);
        await CorrectExamplesAsync(conn, sourceCode, languageCode, ct);
    }

    public async Task<string> CleanAsync(
        string text,
        string? languageCode = null,
        bool applyAutoCorrection = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !applyAutoCorrection)
            return text;

        languageCode ??= DetectLanguageSafe(text);

        var result = await _corrector.AutoCorrectAsync(text, languageCode, ct);
        return string.IsNullOrWhiteSpace(result.CorrectedText)
            ? text
            : result.CorrectedText;
    }

    public Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
        => _corrector.CheckAsync(text, languageCode, ct);

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
        => _corrector.AutoCorrectAsync(text, languageCode, ct);

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
        => _corrector.SuggestImprovementsAsync(text, languageCode, ct);

    private async Task CorrectDefinitionsAsync(
        SqlConnection conn,
        string sourceCode,
        string languageCode,
        CancellationToken ct)
    {
        while (true)
        {
            var records = await conn.QueryAsync<DefinitionRecord>(
                SpName,
                new
                {
                    Mode = 1,
                    SourceCode = sourceCode,
                    MinLength = _settings.MinDefinitionLength,
                    WorkerId,
                    BatchSize = ClaimBatchSize
                },
                commandType: CommandType.StoredProcedure,
                commandTimeout: DbTimeoutSeconds);

            if (!records.Any())
                break;

            await RunParallelCorrectionAsync(
                records,
                r => r.Definition,
                r => r.DictionaryEntryParsedId,
                updateMode: 5,
                languageCode,
                conn,
                ct);
        }
    }

    private async Task CorrectExamplesAsync(
        SqlConnection conn,
        string sourceCode,
        string languageCode,
        CancellationToken ct)
    {
        while (true)
        {
            var records = await conn.QueryAsync<ExampleRecord>(
                SpName,
                new
                {
                    Mode = 3,
                    SourceCode = sourceCode,
                    WorkerId,
                    BatchSize = ClaimBatchSize
                },
                commandType: CommandType.StoredProcedure,
                commandTimeout: DbTimeoutSeconds);

            if (!records.Any())
                break;

            await RunParallelCorrectionAsync(
                records,
                r => r.ExampleText,
                r => r.DictionaryEntryExampleId,
                updateMode: 6,
                languageCode,
                conn,
                ct);
        }
    }

    private static DataTable CreateBatchTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("Id", typeof(long));
        dt.Columns.Add("CorrectedText", typeof(string));
        dt.Columns.Add("ConfidenceScore", typeof(int));
        dt.Columns.Add("EnginesApplied", typeof(string));
        dt.Columns.Add("CorrectionsJson", typeof(string));
        return dt;
    }

    private static async Task FlushBatchAsync(
        SqlConnection conn,
        int mode,
        DataTable batch,
        CancellationToken ct)
    {
        if (batch.Rows.Count == 0)
            return;

        await conn.ExecuteAsync(
            SpName,
            new { Mode = mode, Batch = batch },
            commandType: CommandType.StoredProcedure,
            commandTimeout: DbTimeoutSeconds);

        batch.Clear();
    }

    private static GrammarMeta BuildMeta(GrammarCorrectionResult result)
    {
        if (result.AppliedCorrections == null || result.AppliedCorrections.Count == 0)
            return new GrammarMeta(100, string.Empty, "[]");

        var confidence = (int)Math.Clamp(
            Math.Round(result.AppliedCorrections.Average(x => x.Confidence)),
            0, 100);

        var engines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in result.AppliedCorrections)
        {
            if (c.RuleId.StartsWith("REGEX_", StringComparison.OrdinalIgnoreCase))
                engines.Add("CustomRuleEngine");
            else if (c.RuleId.StartsWith("HUNSPELL_", StringComparison.OrdinalIgnoreCase))
                engines.Add("Hunspell");
            else
                engines.Add("LanguageTool");
        }

        return new GrammarMeta(
            confidence,
            string.Join(", ", engines.OrderBy(x => x)),
            JsonSerializer.Serialize(result.AppliedCorrections));
    }

    private string DetectLanguageSafe(string text)
    {
        try
        {
            return _languageDetector.Detect(text) ?? _settings.DefaultLanguage;
        }
        catch
        {
            return _settings.DefaultLanguage;
        }
    }

    private async Task RunParallelCorrectionAsync<T>(
        IEnumerable<T> records,
        Func<T, string> textSelector,
        Func<T, long> idSelector,
        int updateMode,
        string languageCode,
        SqlConnection conn,
        CancellationToken ct)
    {
        var batch = CreateBatchTable();
        var batchLock = new object();

        await Parallel.ForEachAsync(
            records,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelism,
                CancellationToken = ct
            },
            async (record, token) =>
            {
                var original = textSelector(record);
                if (string.IsNullOrWhiteSpace(original))
                    return;

                GrammarCorrectionResult result;

                try
                {
                    result = await _corrector.AutoCorrectAsync(original, languageCode, token);
                }
                catch
                {
                    return;
                }

                var finalText = string.IsNullOrWhiteSpace(result.CorrectedText)
                    ? original
                    : result.CorrectedText;

                var meta = BuildMeta(result);

                lock (batchLock)
                {
                    batch.Rows.Add(
                        idSelector(record),
                        finalText,
                        meta.ConfidenceScore,
                        meta.EnginesApplied,
                        meta.CorrectionsJson);

                    if (batch.Rows.Count >= BatchSize)
                    {
                        FlushBatchAsync(conn, updateMode, batch, token)
                            .GetAwaiter()
                            .GetResult();
                    }
                }
            });

        await FlushBatchAsync(conn, updateMode, batch, ct);
    }

    private sealed record DefinitionRecord(long DictionaryEntryParsedId, string Definition, string Word);
    private sealed record ExampleRecord(long DictionaryEntryExampleId, string ExampleText, string Word);
    private sealed record GrammarMeta(
        int ConfidenceScore,
        string EnginesApplied,
        string CorrectionsJson);
}