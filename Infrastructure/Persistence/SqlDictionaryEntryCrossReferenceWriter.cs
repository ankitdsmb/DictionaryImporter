namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryCrossReferenceWriter(
        string connectionString,
        ILogger<SqlDictionaryEntryCrossReferenceWriter> logger) : IDictionaryEntryCrossReferenceWriter
    {
        public async Task WriteAsync(
            long parsedDefinitionId,
            CrossReference crossRef,
            string sourceCode,
            CancellationToken ct)
        {
            if (crossRef == null)
                throw new ArgumentNullException(nameof(crossRef));

            if (parsedDefinitionId <= 0)
                return;

            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            var target = NormalizeTargetWord(crossRef.TargetWord);
            if (string.IsNullOrWhiteSpace(target))
                return;

            var type = NormalizeReferenceType(crossRef.ReferenceType);
            if (string.IsNullOrWhiteSpace(type))
                type = "related";

            const string sql = """
                               INSERT INTO dbo.DictionaryEntryCrossReference
                               (
                                   SourceParsedId,
                                   TargetWord,
                                   ReferenceType,
                                   SourceCode
                               )
                               SELECT
                                   @ParsedId,
                                   @Target,
                                   @Type,
                                   @SourceCode
                               WHERE NOT EXISTS
                               (
                                   SELECT 1
                                   FROM dbo.DictionaryEntryCrossReference x WITH (NOLOCK)
                                   WHERE x.SourceParsedId = @ParsedId
                                     AND x.TargetWord = @Target
                                     AND x.ReferenceType = @Type
                                     AND x.SourceCode = @SourceCode
                               );
                               """;

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            try
            {
                var rows =
                    await conn.ExecuteAsync(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                ParsedId = parsedDefinitionId,
                                Target = target,
                                Type = type,
                                SourceCode = sourceCode
                            },
                            cancellationToken: ct));

                if (rows > 0)
                {
                    logger.LogDebug(
                        "CrossReference inserted | ParsedId={ParsedId} | Type={Type} | Target={Target} | SourceCode={SourceCode}",
                        parsedDefinitionId,
                        type,
                        target,
                        sourceCode);
                }
            }
            catch (Exception ex)
            {
                // ✅ Never crash import
                logger.LogDebug(
                    ex,
                    "Failed to insert CrossReference | ParsedId={ParsedId} | Type={Type} | Target={Target} | SourceCode={SourceCode}",
                    parsedDefinitionId,
                    type,
                    target,
                    sourceCode);
            }
        }

        // NEW METHOD (added)
        private static string NormalizeTargetWord(string? targetWord)
        {
            if (string.IsNullOrWhiteSpace(targetWord))
                return string.Empty;

            var t = targetWord.Trim();

            // Remove wiki/template remnants or broken tokens
            t = t.Replace("[[", "").Replace("]]", "");
            t = t.Replace("{{", "").Replace("}}", "");
            t = t.Replace("|", " ");

            // normalize whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // remove surrounding punctuation
            t = t.Trim('\"', '\'', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}');

            // must contain at least one letter
            if (!Regex.IsMatch(t, @"[A-Za-z]"))
                return string.Empty;

            // limit to prevent DB issues / junk
            if (t.Length > 80)
                t = t.Substring(0, 80).Trim();

            return t.ToLowerInvariant();
        }

        // NEW METHOD (added)
        private static string NormalizeReferenceType(string? referenceType)
        {
            if (string.IsNullOrWhiteSpace(referenceType))
                return string.Empty;

            var t = referenceType.Trim();

            // normalize whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // avoid very long or noisy types
            if (t.Length > 50)
                t = t.Substring(0, 50).Trim();

            return t.ToLowerInvariant();
        }
    }
}
