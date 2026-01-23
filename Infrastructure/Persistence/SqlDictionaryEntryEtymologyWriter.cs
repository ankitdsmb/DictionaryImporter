namespace DictionaryImporter.Infrastructure.Persistence
{
    public class SqlDictionaryEntryEtymologyWriter(
        string connectionString,
        ILogger<SqlDictionaryEntryEtymologyWriter> logger)
        : IEntryEtymologyWriter
    {
        public async Task WriteAsync(DictionaryEntryEtymology etymology, CancellationToken ct)
        {
            if (etymology == null)
                throw new ArgumentNullException(nameof(etymology));

            if (etymology.DictionaryEntryId <= 0)
                return;

            var sourceCode = string.IsNullOrWhiteSpace(etymology.SourceCode)
                ? "UNKNOWN"
                : etymology.SourceCode.Trim();

            var etymologyText = NormalizeEtymologyText(etymology.EtymologyText);

            if (string.IsNullOrWhiteSpace(etymologyText))
                return;

            // ✅ LanguageCode is init-only -> do NOT assign back to object
            var languageCode = string.IsNullOrWhiteSpace(etymology.LanguageCode)
                ? null
                : etymology.LanguageCode.Trim();

            const string sql = """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DictionaryEntryEtymology WITH (NOLOCK)
                    WHERE DictionaryEntryId = @DictionaryEntryId
                      AND SourceCode = @SourceCode
                      AND ISNULL(LanguageCode, '') = ISNULL(@LanguageCode, '')
                      AND EtymologyText = @EtymologyText
                )
                BEGIN
                    INSERT INTO dbo.DictionaryEntryEtymology (
                        DictionaryEntryId, EtymologyText, LanguageCode,
                        SourceCode, CreatedUtc
                    ) VALUES (
                        @DictionaryEntryId, @EtymologyText, @LanguageCode,
                        @SourceCode, SYSUTCDATETIME()
                    );
                END
                """;

            var parameters = new
            {
                DictionaryEntryId = etymology.DictionaryEntryId,
                EtymologyText = etymologyText,
                LanguageCode = languageCode,
                SourceCode = sourceCode
            };

            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(ct);

                await connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, cancellationToken: ct));

                logger.LogDebug(
                    "Wrote etymology (if missing) for DictionaryEntryId={EntryId} | SourceCode={SourceCode}",
                    etymology.DictionaryEntryId,
                    sourceCode);
            }
            catch (Exception ex)
            {
                // ✅ Never crash importer
                logger.LogDebug(
                    ex,
                    "Failed to write etymology for DictionaryEntryId={EntryId} | SourceCode={SourceCode}",
                    etymology.DictionaryEntryId,
                    sourceCode);
            }
        }

        // NEW METHOD (added)
        private static string NormalizeEtymologyText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim();

            t = Regex.Replace(t, @"\s+", " ").Trim();

            if (t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (t.Length < 3)
                return string.Empty;

            if (t.Length > 4000)
                t = t.Substring(0, 4000).Trim();

            return t;
        }
    }
}
