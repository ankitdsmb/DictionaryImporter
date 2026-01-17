namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterTransformer(ILogger<GutenbergWebsterTransformer> logger)
        : IDataTransformer<GutenbergRawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(
            GutenbergRawEntry raw)
        {
            logger.LogDebug(
                "Transforming headword {Word}", raw.Headword);

            var (headerPos, _) =
                WebsterHeaderPosExtractor.Extract(
                    string.Join(" ", raw.Lines));

            if (headerPos != null)
                logger.LogDebug(
                    "Header POS resolved | Word={Word} | POS={POS}",
                    raw.Headword,
                    headerPos);

            var seen =
                new HashSet<string>(StringComparer.Ordinal);

            var sense = 1;

            foreach (var def in ExtractDefinitions(raw.Lines))
            {
                var normalizedDef =
                    NormalizeDefinition(def);

                var dedupKey =
                    $"{raw.Headword.ToLowerInvariant()}|{sense}|{normalizedDef}";

                if (!seen.Add(dedupKey))
                {
                    logger.LogDebug(
                        "Skipped duplicate definition for {Word}, sense {Sense}",
                        raw.Headword,
                        sense);
                    continue;
                }

                yield return new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord =
                        raw.Headword.ToLowerInvariant(),
                    Definition = def,
                    SenseNumber = sense,
                    SourceCode = "GUT_WEBSTER",
                    PartOfSpeech = headerPos,
                    CreatedUtc = DateTime.UtcNow
                };

                sense++;
            }
        }

        private static IEnumerable<string> ExtractDefinitions(
            List<string> lines)
        {
            var buffer = new List<string>();

            foreach (var line in lines)
                if (line.StartsWith("Defn:"))
                {
                    if (buffer.Count > 0)
                        yield return string.Join(" ", buffer);

                    buffer.Clear();
                    buffer.Add(line[5..].Trim());
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    buffer.Add(line.Trim());
                }

            if (buffer.Count > 0)
                yield return string.Join(" ", buffer);
        }

        private static string NormalizeDefinition(
            string text)
        {
            text = text.ToLowerInvariant();

            text =
                Regex.Replace(
                    text,
                    @"\s+",
                    " ");

            return text.Trim(
                ' ',
                '.',
                ';',
                ':');
        }
    }
}