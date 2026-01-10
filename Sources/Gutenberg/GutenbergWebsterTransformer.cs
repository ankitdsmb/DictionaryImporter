using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterTransformer
        : IDataTransformer<GutenbergRawEntry>
    {
        private readonly ILogger<GutenbergWebsterTransformer> _logger;

        public GutenbergWebsterTransformer(
            ILogger<GutenbergWebsterTransformer> logger)
        {
            _logger = logger;
        }

        public IEnumerable<DictionaryEntry> Transform(
            GutenbergRawEntry raw)
        {
            _logger.LogDebug(
                "Transforming headword {Word}", raw.Headword);

            // -------------------------------------------------
            // HEADER POS (EXTRACT ONCE, APPLY TO ALL SENSES)
            // -------------------------------------------------
            var (headerPos, _) =
                WebsterHeaderPosExtractor.Extract(
                    string.Join(" ", raw.Lines));

            if (headerPos != null)
            {
                _logger.LogDebug(
                    "Header POS resolved | Word={Word} | POS={POS}",
                    raw.Headword,
                    headerPos);
            }

            var seen =
                new HashSet<string>(StringComparer.Ordinal);

            int sense = 1;

            foreach (var def in ExtractDefinitions(raw.Lines))
            {
                var normalizedDef =
                    NormalizeDefinition(def);

                // ---------------------------------------------
                // TRANSFORM-LEVEL DEDUPLICATION
                // ---------------------------------------------
                var dedupKey =
                    $"{raw.Headword.ToLowerInvariant()}|{sense}|{normalizedDef}";

                if (!seen.Add(dedupKey))
                {
                    _logger.LogDebug(
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
                    PartOfSpeech = headerPos, // APPLY TO ALL SENSES
                    CreatedUtc = DateTime.UtcNow
                };

                sense++;
            }
        }

        // =====================================================
        // DEFINITION EXTRACTION
        // =====================================================
        private static IEnumerable<string> ExtractDefinitions(
            List<string> lines)
        {
            var buffer = new List<string>();

            foreach (var line in lines)
            {
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
            }

            if (buffer.Count > 0)
                yield return string.Join(" ", buffer);
        }

        // =====================================================
        // NORMALIZATION (DEDUP ONLY)
        // =====================================================
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
