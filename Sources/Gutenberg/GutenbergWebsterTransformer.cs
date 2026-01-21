using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterTransformer(ILogger<GutenbergWebsterTransformer> logger) : IDataTransformer<GutenbergRawEntry>
    {
        private const string SourceCode = "GUT_WEBSTER";

        public IEnumerable<DictionaryEntry> Transform(GutenbergRawEntry raw)
        {
            if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger)) yield break;

            logger.LogDebug("Transforming headword {Word}", raw.Headword);

            foreach (var entry in ProcessGutenbergEntry(raw))
                yield return entry;
        }

        private IEnumerable<DictionaryEntry> ProcessGutenbergEntry(GutenbergRawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                var (headerPos, _) = WebsterHeaderPosExtractor.Extract(string.Join(" ", raw.Lines));
                if (headerPos != null)
                    logger.LogDebug("Header POS resolved | Word={Word} | POS={POS}", raw.Headword, headerPos);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var normalizedWord = SourceDataHelper.NormalizeWord(raw.Headword);
                var rawFragment = string.Join("\n", raw.Lines);
                var sense = 1;

                foreach (var def in ExtractDefinitions(raw.Lines))
                {
                    var normalizedDef = SourceDataHelper.NormalizeDefinition(def);

                    // FIX: Do not include sense in dedup key (sense changes when duplicates are skipped)
                    var dedupKey = $"{normalizedWord}|{normalizedDef}";
                    if (!seen.Add(dedupKey))
                    {
                        logger.LogDebug("Skipped duplicate definition for {Word}, sense {Sense}", raw.Headword, sense);
                        continue;
                    }

                    entries.Add(new DictionaryEntry
                    {
                        Word = raw.Headword,
                        NormalizedWord = normalizedWord,
                        Definition = def,
                        RawFragment = rawFragment,
                        SenseNumber = sense,
                        SourceCode = SourceCode,
                        PartOfSpeech = headerPos,
                        CreatedUtc = DateTime.UtcNow
                    });
                    sense++;
                }

                SourceDataHelper.LogProgress(logger, SourceCode, SourceDataHelper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                SourceDataHelper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }

        private static IEnumerable<string> ExtractDefinitions(List<string> lines)
        {
            var buffer = new List<string>();
            var started = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("Defn:"))
                {
                    started = true;
                    if (buffer.Count > 0)
                    {
                        yield return string.Join(" ", buffer);
                        buffer.Clear();
                    }
                    buffer.Add(line[5..].Trim());
                    continue;
                }

                // FIX: Also handle other definition markers in Gutenberg Webster format
                if (line.StartsWith("Etym:"))
                {
                    // Etymology section - start a new definition if we have content
                    if (buffer.Count > 0)
                    {
                        yield return string.Join(" ", buffer);
                        buffer.Clear();
                    }
                    started = true;
                    buffer.Add(line.Trim());
                    continue;
                }

                // FIX: Handle numbered definitions like "1.", "2.", etc.
                if (Regex.IsMatch(line, @"^\d+\.\s+"))
                {
                    if (buffer.Count > 0)
                    {
                        yield return string.Join(" ", buffer);
                        buffer.Clear();
                    }
                    started = true;
                    buffer.Add(line.Trim());
                    continue;
                }

                // FIX: Ignore anything before first Defn: or other content marker
                if (!started) continue;

                if (!string.IsNullOrWhiteSpace(line))
                    buffer.Add(line.Trim());
            }

            if (buffer.Count > 0)
                yield return string.Join(" ", buffer);
        }
    }
}