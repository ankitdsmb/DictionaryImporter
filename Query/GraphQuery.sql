--1.1 Get all senses of a word
SELECT ToNodeId
FROM dbo.GraphEdge
WHERE FromNodeId = CONCAT('Word:', @CanonicalWordId)
  AND RelationType = 'HAS_SENSE';

--1.2 Count senses for a word
SELECT COUNT(*)
FROM dbo.GraphEdge
WHERE FromNodeId = CONCAT('Word:', @CanonicalWordId)
  AND RelationType = 'HAS_SENSE';

--1.3 Check if a word has any senses
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.GraphEdge
    WHERE FromNodeId = CONCAT('Word:', @CanonicalWordId)
      AND RelationType = 'HAS_SENSE'
) THEN 1 ELSE 0 END;
--2.1 Get “See” references
SELECT ToNodeId
FROM dbo.GraphEdge
WHERE FromNodeId = CONCAT('Sense:', @SenseId)
  AND RelationType = 'SEE';

--2.2 Get “Related / See also”
SELECT ToNodeId
FROM dbo.GraphEdge
WHERE FromNodeId = CONCAT('Sense:', @SenseId)
  AND RelationType = 'RELATED_TO';

--2.3 Get “Compare (Cf.)”
SELECT ToNodeId
FROM dbo.GraphEdge
WHERE FromNodeId = CONCAT('Sense:', @SenseId)
  AND RelationType = 'COMPARE';
--Get all related senses (any relation)
SELECT ToNodeId, RelationType
FROM dbo.GraphEdge
WHERE FromNodeId = CONCAT('Sense:', @SenseId)
  AND RelationType IN ('SEE', 'RELATED_TO', 'COMPARE');
--2.5 Reverse lookup (who references this sense)
SELECT FromNodeId, RelationType
FROM dbo.GraphEdge
WHERE ToNodeId = CONCAT('Sense:', @SenseId)
  AND RelationType IN ('SEE', 'RELATED_TO', 'COMPARE');


----Get all words associated with a concept
SELECT DISTINCT e.Word
FROM dbo.GraphEdge g
JOIN dbo.DictionaryEntryParsed p
  ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
JOIN dbo.DictionaryEntry e
  ON e.DictionaryEntryId = p.DictionaryEntryId
WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId);

--4. DEPTH-LIMITED TRAVERSAL (CORE QUERY)
--4.1 Traverse from a node up to N hops
WITH GraphWalk AS
(
    -- Anchor
    SELECT
        g.FromNodeId,
        g.ToNodeId,
        g.RelationType,
        1 AS Depth,
        CAST(g.FromNodeId + '>' + g.ToNodeId AS NVARCHAR(MAX)) AS Path
    FROM dbo.GraphEdge g
    WHERE g.FromNodeId = @StartNode

    UNION ALL

    -- Recursive step
    SELECT
        gw.FromNodeId,
        g.ToNodeId,
        g.RelationType,
        gw.Depth + 1,
        CAST(gw.Path + '>' + g.ToNodeId AS NVARCHAR(MAX))
    FROM GraphWalk gw
    JOIN dbo.GraphEdge g
        ON g.FromNodeId = gw.ToNodeId
    WHERE gw.Depth < @MaxDepth
      AND CHARINDEX(g.ToNodeId, gw.Path) = 0  -- cycle prevention
)
SELECT *
FROM GraphWalk;

--6. SHORTEST PATH BETWEEN TWO NODES
--6.1 Find minimal semantic path
WITH PathSearch AS
(
    SELECT
        g.FromNodeId,
        g.ToNodeId,
        CAST(g.FromNodeId + '>' + g.ToNodeId AS NVARCHAR(MAX)) AS Path,
        1 AS Depth
    FROM dbo.GraphEdge g
    WHERE g.FromNodeId = @StartNode

    UNION ALL

    SELECT
        ps.FromNodeId,
        g.ToNodeId,
        CAST(ps.Path + '>' + g.ToNodeId AS NVARCHAR(MAX)),
        ps.Depth + 1
    FROM PathSearch ps
    JOIN dbo.GraphEdge g
        ON g.FromNodeId = ps.ToNodeId
    WHERE ps.Depth < @MaxDepth
      AND CHARINDEX(g.ToNodeId, ps.Path) = 0
)
SELECT TOP 1 *
FROM PathSearch
WHERE ToNodeId = @TargetNode
ORDER BY Depth;
--7. INFLUENCE RADIUS (NEIGHBORHOOD)
--7.1 All nodes reachable within N hops
WITH Reach AS
(
    SELECT
        ToNodeId,
        1 AS Depth
    FROM dbo.GraphEdge
    WHERE FromNodeId = @StartNode

    UNION ALL

    SELECT
        g.ToNodeId,
        r.Depth + 1
    FROM Reach r
    JOIN dbo.GraphEdge g
        ON g.FromNodeId = r.ToNodeId
    WHERE r.Depth < @MaxDepth
)
SELECT DISTINCT ToNodeId
FROM Reach;
