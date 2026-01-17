namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptConfidenceCalculator(
        string connectionString,
        ILogger<DictionaryConceptConfidenceCalculator> logger)
    {
        public async Task CalculateAsync(
            CancellationToken ct)
        {
            logger.LogInformation(
                "ConceptConfidence calculation started");

            await using var conn =
                new SqlConnection(connectionString);

            await conn.OpenAsync(ct);

            var concepts =
                (await conn.QueryAsync<long>(
                    "SELECT ConceptId FROM dbo.Concept"))
                .ToList();

            logger.LogInformation(
                "ConceptConfidence | TotalConcepts={Count}",
                concepts.Count);

            var processed = 0;

            foreach (var conceptId in concepts)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                if (processed % 1_000 == 0)
                    logger.LogInformation(
                        "ConceptConfidence progress | Processed={Processed}/{Total}",
                        processed,
                        concepts.Count);

                var senseCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*)
                        FROM dbo.GraphEdge
                        WHERE ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreA =
                    Math.Min(senseCount / 5.0, 1.0);

                var sourceCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(DISTINCT e.SourceCode)
                        FROM dbo.GraphEdge g
                        JOIN dbo.DictionaryEntryParsed p
                            ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                        JOIN dbo.DictionaryEntry e
                            ON e.DictionaryEntryId = p.DictionaryEntryId
                        WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreB =
                    Math.Min(sourceCount / 3.0, 1.0);

                var crossRefCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*)
                        FROM dbo.GraphEdge
                        WHERE FromNodeId IN (
                            SELECT FromNodeId
                            FROM dbo.GraphEdge
                            WHERE ToNodeId = CONCAT('Concept:', @ConceptId)
                              AND RelationType = 'BELONGS_TO'
                        )
                        AND RelationType IN ('SEE', 'RELATED_TO', 'COMPARE')
                        """,
                        new { ConceptId = conceptId });

                var scoreC =
                    Math.Min(crossRefCount / 5.0, 1.0);

                var domainCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(DISTINCT p.Domain)
                        FROM dbo.GraphEdge g
                        JOIN dbo.DictionaryEntryParsed p
                            ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                        WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreD =
                    domainCount <= 1 ? 1.0 : 0.5;

                var posCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(DISTINCT e.PartOfSpeech)
                        FROM dbo.GraphEdge g
                        JOIN dbo.DictionaryEntryParsed p
                            ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                        JOIN dbo.DictionaryEntry e
                            ON e.DictionaryEntryId = p.DictionaryEntryId
                        WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreE =
                    posCount <= 1 ? 1.0 : 0.0;

                var confidence =
                    scoreA * 0.30 +
                    scoreB * 0.25 +
                    scoreC * 0.20 +
                    scoreD * 0.15 +
                    scoreE * 0.10;

                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.Concept
                    SET ConfidenceScore = @Score
                    WHERE ConceptId = @ConceptId
                    """,
                    new
                    {
                        ConceptId = conceptId,
                        Score = confidence
                    });
            }

            logger.LogInformation(
                "ConceptConfidence calculation completed | TotalProcessed={Total}",
                processed);
        }
    }
}