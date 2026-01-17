namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptBuilder(
        string connectionString,
        ILogger<DictionaryConceptBuilder> logger)
    {
        public async Task BuildAsync(
            string sourceCode,
            CancellationToken ct)
        {
            logger.LogInformation(
                "ConceptBuilder started | Source={Source}",
                sourceCode);

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var senses = (
                await conn.QueryAsync<ConceptSeed>(
                    """
                    SELECT
                        p.DictionaryEntryParsedId,
                        LOWER(ISNULL(p.Domain, 'gen'))       AS Domain,
                        LOWER(ISNULL(e.PartOfSpeech, 'unk')) AS PartOfSpeech,
                        LOWER(e.NormalizedWord)              AS Head
                    FROM dbo.DictionaryEntryParsed p
                    JOIN dbo.DictionaryEntry e
                        ON e.DictionaryEntryId = p.DictionaryEntryId
                    WHERE e.SourceCode = @SourceCode;
                    """,
                    new { SourceCode = sourceCode })
            ).ToList();

            logger.LogInformation(
                "ConceptBuilder | Source={Source} | CandidateSenses={Count}",
                sourceCode,
                senses.Count);

            var processed = 0;

            foreach (var s in senses)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                if (processed % 10_000 == 0)
                    logger.LogInformation(
                        "ConceptBuilder progress | Source={Source} | Processed={Processed}/{Total}",
                        sourceCode,
                        processed,
                        senses.Count);

                var conceptKey =
                    $"{s.Domain}:{s.PartOfSpeech}:{s.Head}";

                await using var tx = await conn.BeginTransactionAsync(ct);

                var conceptId =
                    await conn.ExecuteScalarAsync<long>(
                        """
                        DECLARE @Id bigint;

                        SELECT @Id = ConceptId
                        FROM dbo.Concept
                        WHERE ConceptKey = @Key;

                        IF @Id IS NULL
                        BEGIN
                            INSERT INTO dbo.Concept
                                (ConceptKey, Domain, PartOfSpeech, CreatedUtc)
                            VALUES
                                (@Key, @Domain, @Pos, SYSUTCDATETIME());

                            SET @Id = SCOPE_IDENTITY();
                        END

                        SELECT @Id;
                        """,
                        new
                        {
                            Key = conceptKey,
                            s.Domain,
                            Pos = s.PartOfSpeech
                        },
                        tx);

                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.GraphNode
                        (NodeId, NodeType, RefId, CreatedUtc)
                    SELECT
                        CONCAT('Concept:', @ConceptId),
                        'Concept',
                        @ConceptId,
                        SYSUTCDATETIME()
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphNode
                        WHERE NodeId = CONCAT('Concept:', @ConceptId)
                    );
                    """,
                    new { ConceptId = conceptId },
                    tx);

                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.GraphEdge
                        (FromNodeId, ToNodeId, RelationType, CreatedUtc)
                    SELECT
                        CONCAT('Sense:', @SenseId),
                        CONCAT('Concept:', @ConceptId),
                        'BELONGS_TO',
                        SYSUTCDATETIME()
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphEdge g
                        WHERE g.FromNodeId   = CONCAT('Sense:', @SenseId)
                          AND g.ToNodeId     = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                    );
                    """,
                    new
                    {
                        SenseId = s.DictionaryEntryParsedId,
                        ConceptId = conceptId
                    },
                    tx);

                await tx.CommitAsync(ct);
            }

            logger.LogInformation(
                "ConceptBuilder completed | Source={Source} | TotalProcessed={Total}",
                sourceCode,
                processed);
        }

        private sealed class ConceptSeed
        {
            public long DictionaryEntryParsedId { get; init; }
            public string Domain { get; init; } = null!;
            public string PartOfSpeech { get; init; } = null!;
            public string Head { get; init; } = null!;
        }
    }
}