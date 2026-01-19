using System;
using System.Collections.Generic;
using System.Linq;

namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class OxfordParsingHelper
    {
        public static IReadOnlyList<string> ExtractExamples(string definition)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return examples;

            var lines = definition.Split('\n');
            var inExamplesSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("【Examples】"))
                {
                    inExamplesSection = true;
                    continue;
                }

                if (inExamplesSection)
                {
                    if (trimmed.StartsWith("【") || string.IsNullOrEmpty(trimmed))
                        break;

                    if (trimmed.StartsWith("»"))
                        examples.Add(trimmed[1..].Trim());
                }
            }

            return examples;
        }

        public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
        {
            var crossRefs = new List<CrossReference>();

            var seeAlsoSection = SourceDataHelper.ExtractSection(definition, "【SeeAlso】");
            if (string.IsNullOrEmpty(seeAlsoSection))
                return crossRefs;

            var references = seeAlsoSection.Split(';', StringSplitOptions.RemoveEmptyEntries);

            crossRefs.AddRange(from refWord in references select refWord.Trim() into trimmed where !string.IsNullOrEmpty(trimmed) select new CrossReference { TargetWord = trimmed, ReferenceType = "SeeAlso" });

            return crossRefs;
        }
    }
}