using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Domain.Models;
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

            var seen =
                new HashSet<string>(StringComparer.Ordinal);

            int sense = 1;

            foreach (var def in ExtractDefinitions(raw.Lines))
            {
                var normalizedDef =
                    NormalizeDefinition(def);

                // ---------------------------------------------
                // TRANSFORM-LEVEL DEDUPLICATION (SAFE)
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

        private static string NormalizeDefinition(string text)
        {
            text = text.ToLowerInvariant();

            // collapse whitespace
            text = Regex.Replace(text, @"\s+", " ");

            // remove trailing punctuation noise
            text = text.Trim(' ', '.', ';', ':');

            return text;
        }
    }
}