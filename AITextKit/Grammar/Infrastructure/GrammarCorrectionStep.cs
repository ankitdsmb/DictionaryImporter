namespace DictionaryImporter.AITextKit.Grammar.Infrastructure;

public sealed class GrammarCorrectionStep(
    string connectionString,
    IGrammarCorrector grammarCorrector,
    EnhancedGrammarConfiguration settings,
    ILogger<GrammarCorrectionStep> logger)
{
    public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
    {
        if (!settings.Enabled || !settings.EnabledForSource(sourceCode))
        {
            logger.LogInformation("Grammar correction disabled for source {Source}", sourceCode);
            return;
        }

        logger.LogInformation("Grammar correction started | Source={Source}", sourceCode);

        try
        {
            await CorrectDefinitions(sourceCode, ct);

            await CorrectExamples(sourceCode, ct);

            logger.LogInformation("Grammar correction completed | Source={Source}", sourceCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Grammar correction failed for source {Source}", sourceCode);
            throw;
        }
    }

    /// <summary>
    /// Correct all definitions for a source
    /// </summary>
    private async Task CorrectDefinitions(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var definitions = await conn.QueryAsync<DefinitionRecord>(
            """
            SELECT p.DictionaryEntryParsedId, p.Definition, e.Word
            FROM dbo.DictionaryEntryParsed p
            JOIN dbo.DictionaryEntry e ON e.DictionaryEntryId = p.DictionaryEntryId
            WHERE e.SourceCode = @SourceCode
              AND LEN(p.Definition) > @MinLength
              AND (p.GrammarCorrected = 0 OR p.GrammarCorrected IS NULL)
            ORDER BY p.DictionaryEntryParsedId
            """,
            new
            {
                SourceCode = sourceCode,
                MinLength = settings.MinDefinitionLength
            });

        var total = 0;
        var corrected = 0;
        var languageCode = settings.GetLanguageCode(sourceCode);

        foreach (var record in definitions)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            try
            {
                var result = await grammarCorrector.AutoCorrectAsync(record.Definition, languageCode, ct);

                if (result.CorrectedText != record.Definition)
                {
                    await conn.ExecuteAsync(
                        """
                        UPDATE dbo.DictionaryEntryParsed
                        SET Definition = @CorrectedDefinition,
                            GrammarCorrected = 1,
                            GrammarCorrectionDate = SYSUTCDATETIME()
                        WHERE DictionaryEntryParsedId = @ParsedId
                        """,
                        new
                        {
                            ParsedId = record.DictionaryEntryParsedId,
                            CorrectedDefinition = result.CorrectedText
                        });

                    corrected++;

                    if (corrected % 100 == 0)
                    {
                        logger.LogDebug(
                            "Definition correction progress | Source={Source} | Corrected={Count}",
                            sourceCode, corrected);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to correct definition for word '{Word}' (ID: {Id})",
                    record.Word, record.DictionaryEntryParsedId);
            }
        }

        logger.LogInformation(
            "Definitions corrected | Source={Source} | Total={Total} | Corrected={Corrected}",
            sourceCode, total, corrected);
    }

    /// <summary>
    /// Correct all examples for a source
    /// </summary>
    private async Task CorrectExamples(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var examples = await conn.QueryAsync<ExampleRecord>(
            """
            SELECT e.DictionaryEntryExampleId, e.ExampleText, de.Word
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
        var corrected = 0;
        var languageCode = settings.GetLanguageCode(sourceCode);

        foreach (var record in examples)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            try
            {
                var result = await grammarCorrector.AutoCorrectAsync(record.ExampleText, languageCode, ct);

                if (result.CorrectedText != record.ExampleText)
                {
                    await conn.ExecuteAsync(
                        """
                        UPDATE dbo.DictionaryEntryExample
                        SET ExampleText = @CorrectedText,
                            GrammarCorrected = 1,
                            GrammarCorrectionDate = SYSUTCDATETIME()
                        WHERE DictionaryEntryExampleId = @ExampleId
                        """,
                        new
                        {
                            ExampleId = record.DictionaryEntryExampleId,
                            result.CorrectedText
                        });

                    corrected++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to correct example for word '{Word}' (ID: {Id})",
                    record.Word, record.DictionaryEntryExampleId);
            }
        }

        logger.LogInformation(
            "Examples corrected | Source={Source} | Total={Total} | Corrected={Corrected}",
            sourceCode, total, corrected);
    }

    /// <summary>
    /// Quick grammar check for a single text
    /// </summary>
    public async Task<GrammarCheckResult> QuickCheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        return await grammarCorrector.CheckAsync(text, languageCode, ct);
    }

    private sealed record DefinitionRecord(
        long DictionaryEntryParsedId,
        string Definition,
        string Word);

    private sealed record ExampleRecord(
        long DictionaryEntryExampleId,
        string ExampleText,
        string Word);
}