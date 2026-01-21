namespace DictionaryImporter.Infrastructure.Canonicalization
{
    public sealed class SqlCanonicalWordResolver(
        string connectionString,
        ILogger<SqlCanonicalWordResolver> logger) : ICanonicalWordResolver
    {
        public async Task ResolveAsync(string sourceCode, CancellationToken ct)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            logger.LogInformation(
                "CanonicalResolver started | Source={Source}",
                sourceCode);

            try
            {
                // Step 1: Ensure NormalizedWord is populated for all entries
                var updated = await conn.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE dbo.DictionaryEntry
                        SET NormalizedWord = LOWER(LTRIM(RTRIM(Word)))
                        WHERE SourceCode = @SourceCode
                        AND (NormalizedWord IS NULL OR NormalizedWord = '')
                        """,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));

                if (updated > 0)
                {
                    logger.LogInformation(
                        "CanonicalResolver | Updated {Count} entries with missing NormalizedWord",
                        updated);
                }

                // Step 2: Insert new canonical words
                var inserted = await conn.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO dbo.CanonicalWord (NormalizedWord)
                        SELECT DISTINCT e.NormalizedWord
                        FROM dbo.DictionaryEntry e
                        LEFT JOIN dbo.CanonicalWord c ON c.NormalizedWord = e.NormalizedWord
                        WHERE e.SourceCode = @SourceCode
                        AND e.NormalizedWord IS NOT NULL
                        AND e.NormalizedWord <> ''
                        AND c.CanonicalWordId IS NULL
                        AND NOT EXISTS (
                            SELECT 1
                            FROM dbo.CanonicalWord c2
                            WHERE c2.NormalizedWord = e.NormalizedWord
                        )
                        """,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));

                logger.LogInformation(
                    "CanonicalResolver | Inserted {Count} new canonical words",
                    inserted);

                // Step 3: Link entries to canonical words
                var linked = await conn.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE e
                        SET e.CanonicalWordId = c.CanonicalWordId
                        FROM dbo.DictionaryEntry e
                        INNER JOIN dbo.CanonicalWord c ON c.NormalizedWord = e.NormalizedWord
                        WHERE e.SourceCode = @SourceCode
                        AND e.NormalizedWord IS NOT NULL
                        AND e.NormalizedWord <> ''
                        AND e.CanonicalWordId IS NULL
                        """,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));

                logger.LogInformation(
                    "CanonicalResolver | Linked {Count} entries to canonical words",
                    linked);

                // Step 4: Report any entries that couldn't be linked
                var unlinkedCount = await conn.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        """
                        SELECT COUNT(*)
                        FROM dbo.DictionaryEntry
                        WHERE SourceCode = @SourceCode
                        AND CanonicalWordId IS NULL
                        AND NormalizedWord IS NOT NULL
                        AND NormalizedWord <> ''
                        """,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));

                if (unlinkedCount > 0)
                {
                    logger.LogWarning(
                        "CanonicalResolver | {Count} entries could not be linked to canonical words",
                        unlinkedCount);

                    // Log some examples of unlinked entries
                    var unlinkedExamples = await conn.QueryAsync<string>(
                        new CommandDefinition(
                            """
                            SELECT TOP 5 Word
                            FROM dbo.DictionaryEntry
                            WHERE SourceCode = @SourceCode
                            AND CanonicalWordId IS NULL
                            AND NormalizedWord IS NOT NULL
                            ORDER BY Word
                            """,
                            new { SourceCode = sourceCode },
                            cancellationToken: ct));

                    var enumerable = unlinkedExamples as string[] ?? unlinkedExamples.ToArray();
                    if (enumerable.Any())
                    {
                        logger.LogWarning(
                            "CanonicalResolver | Examples of unlinked entries: {Examples}",
                            string.Join(", ", enumerable));
                    }
                }

                logger.LogInformation(
                    "CanonicalResolver completed successfully | Source={Source}",
                    sourceCode);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "CanonicalResolver FAILED | Source={Source}",
                    sourceCode);
                throw;
            }
        }
    }
}