using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Oxford.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Oxford
{
    public sealed class OxfordTransformer(ILogger<OxfordTransformer> logger)
        : IDataTransformer<OxfordRawEntry>
    {
        private const string SourceCode = "ENG_OXFORD";

        public IEnumerable<DictionaryEntry> Transform(OxfordRawEntry? raw)
        {
            if (raw == null || !raw.Senses.Any())
                yield break;

            foreach (var entry in ProcessOxfordEntry(raw))
            {
                // FIX: apply limit per produced DictionaryEntry (not per raw entry)
                if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger))
                    yield break;

                yield return entry;
            }
        }

        private IEnumerable<DictionaryEntry> ProcessOxfordEntry(OxfordRawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                var normalizedWord = NormalizeWord(raw.Headword);
                var normalizedPos = OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);

                entries.AddRange(from sense in raw.Senses
                                 let fullDefinition = BuildFullDefinition(raw, sense)
                                 select new DictionaryEntry
                                 {
                                     Word = raw.Headword,
                                     NormalizedWord = normalizedWord,
                                     PartOfSpeech = normalizedPos,
                                     Definition = fullDefinition,

                                     // FIX: keep RawFragment truly "raw" so parsers/extractors can rely on it
                                     // Safest is the original sense definition text
                                     RawFragment = sense.Definition,
                                     SenseNumber = sense.SenseNumber,
                                     SourceCode = SourceCode,
                                     CreatedUtc = DateTime.UtcNow
                                 });

                SourceDataHelper.LogProgress(logger, SourceCode, SourceDataHelper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                SourceDataHelper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }

        private static string BuildFullDefinition(OxfordRawEntry entry, OxfordSenseRaw sense)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(entry.Pronunciation))
                parts.Add($"【Pronunciation】{entry.Pronunciation}");

            if (!string.IsNullOrEmpty(entry.VariantForms))
                parts.Add($"【Variants】{entry.VariantForms}");

            if (!string.IsNullOrEmpty(sense.SenseLabel))
                parts.Add($"【Label】{sense.SenseLabel}");

            parts.Add(sense.Definition);

            if (!string.IsNullOrEmpty(sense.ChineseTranslation))
                parts.Add($"【Chinese】{sense.ChineseTranslation}");

            if (sense.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in sense.Examples)
                    parts.Add($"» {example}");
            }

            if (!string.IsNullOrEmpty(sense.UsageNote))
                parts.Add($"【Usage】{sense.UsageNote}");

            if (sense.CrossReferences.Any())
            {
                parts.Add("【SeeAlso】");
                parts.Add(string.Join("; ", sense.CrossReferences));
            }

            return string.Join("\n", parts);
        }

        private static string NormalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return word;

            word = SourceDataHelper.NormalizeWord(word);
            // allow digits too for words like "24-7", "3d", "mp3"
            word = Regex.Replace(word, @"[^\p{L}\p{N}\-']", " ");
            return Regex.Replace(word, @"\s+", " ").Trim();
        }
    }
}