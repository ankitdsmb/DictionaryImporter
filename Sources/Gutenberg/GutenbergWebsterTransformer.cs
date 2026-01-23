using System;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Common;
using DictionaryImporter.Sources.Common.Helper;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterTransformer(ILogger<GutenbergWebsterTransformer> logger)
        : IDataTransformer<GutenbergRawEntry>
    {
        private const string SourceCode = "GUT_WEBSTER";

        public IEnumerable<DictionaryEntry> Transform(GutenbergRawEntry raw)
        {
            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                yield break;

            if (raw == null || string.IsNullOrWhiteSpace(raw.Headword) || raw.Lines == null || raw.Lines.Count == 0)
                yield break;

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
                if (!string.IsNullOrWhiteSpace(headerPos))
                    logger.LogDebug("Header POS resolved | Word={Word} | POS={POS}", raw.Headword, headerPos);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var normalizedWord = Helper.NormalizeWord(raw.Headword);
                var rawFragment = string.Join("\n", raw.Lines);

                var sense = 1;

                foreach (var def in ParsingHelperGutenberg.ExtractDefinitionsFromRawLines(raw.Lines))
                {
                    var cleanedDef = ParsingHelperGutenberg.NormalizeGutenbergTransformerDefinition(def);

                    if (string.IsNullOrWhiteSpace(cleanedDef))
                        continue;

                    var normalizedDef = Helper.NormalizeDefinition(cleanedDef);

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
                        Definition = cleanedDef,
                        RawFragment = rawFragment,
                        SenseNumber = sense,
                        SourceCode = SourceCode,
                        PartOfSpeech = headerPos,
                        CreatedUtc = DateTime.UtcNow
                    });

                    sense++;
                }

                Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                Helper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }
    }
}
