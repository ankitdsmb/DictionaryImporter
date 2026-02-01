// Install: dotnet add package FuzzySharp
using FuzzySharp;

namespace DictionaryImporter.Core.Text.Experimental;

public class DefinitionDeduplicator
{
    public List<string> RemoveSimilarDefinitions(List<string> definitions,
        int threshold = 85)
    {
        var unique = new List<string>();

        foreach (var def in definitions)
        {
            bool isDuplicate = false;
            foreach (var uniqueDef in unique)
            {
                var score = Fuzz.Ratio(def, uniqueDef);
                if (score >= threshold)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
                unique.Add(def);
        }

        return unique;
    }

    public string FindBestDefinition(List<string> definitions, string word)
    {
        // Use token sort ratio for better matching
        var scores = definitions
            .Select(d => new
            {
                Definition = d,
                Score = Fuzz.TokenSortRatio(word, d)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        return scores.FirstOrDefault()?.Definition;
    }
}