using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.FragmentStore;

namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsTransformer(ILogger<CollinsTransformer> logger)
    : IDataTransformer<CollinsRawEntry>
{
    private const string SourceCode = "ENG_COLLINS";

    public IEnumerable<DictionaryEntry> Transform(CollinsRawEntry? raw)
    {
        if (!Helper.ShouldContinueProcessing(SourceCode, logger))
            yield break;

        if (raw == null || !raw.Senses.Any())
            yield break;

        foreach (var entry in ProcessCollinsEntry(raw))
            yield return entry;
    }

    private IEnumerable<DictionaryEntry> ProcessCollinsEntry(CollinsRawEntry raw)
    {
        var entries = new List<DictionaryEntry>();

        try
        {
            var normalizedWord = Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

            foreach (var sense in raw.Senses)
            {
                // Build clean definition
                var cleanDefinition = CleanDefinition(sense.Definition);

                // Build raw fragment (preserve original for parsing)
                var rawFragment = BuildRawFragment(sense);

                // Normalize POS - FIXED: Use CollinsExtractor.NormalizePos
                var pos = !string.IsNullOrWhiteSpace(sense.PartOfSpeech) && sense.PartOfSpeech != "unk"
                    ? CollinsExtractor.NormalizePos(sense.PartOfSpeech)
                    : "unk";

                // Clean examples - ensure we preserve them
                var examples = CleanExamples(sense.Examples?.ToList() ?? new List<string>());

                // Extract IPA if present in headword
                var ipa = ExtractIPA(raw.Headword);

                // Extract domain label
                var domainLabel = CleanDomainLabel(sense.DomainLabel);

                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = pos,
                    Definition = cleanDefinition,
                    RawFragment = RawFragments.Save(SourceCode, rawFragment, Encoding.UTF8, raw.Headword),
                    SenseNumber = sense.SenseNumber,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow,
                    Examples = examples, // Store examples
                    UsageNote = CleanUsageNote(sense.UsageNote),
                    DomainLabel = domainLabel,
                    GrammarInfo = CleanGrammarInfo(sense.GrammarInfo),
                    CrossReference = sense.CrossReference,
                    Ipa = ipa
                });
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

    private static string CleanDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        // Remove Chinese characters
        var cleaned = CollinsExtractor.RemoveChineseCharacters(definition);

        // Remove any leftover Chinese punctuation patterns
        cleaned = Regex.Replace(cleaned, @"^[,\s()""]+|[,\s()""]+$", "");

        // Remove random artifacts
        cleaned = cleaned.Replace("\" \"", " ")
                        .Replace("  ", " ")
                        .Replace(" ; ; ", " ")
                        .Replace(" ; ", " ")
                        .Trim();

        // Remove any remaining example markers
        cleaned = cleaned.Replace("...", "").Replace("•", "");

        // Remove bracket markers and their content
        cleaned = Regex.Replace(cleaned, @"【[^】]*】", " ");

        // Ensure proper ending
        if (!string.IsNullOrEmpty(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?") &&
            !cleaned.Contains("→see:"))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private static List<string> CleanExamples(List<string> examples)
    {
        return examples.Select(e => CleanExample(e))
                      .Where(e => !string.IsNullOrWhiteSpace(e))
                      .Where(e => e.Length > 10)
                      .ToList();
    }

    private static string CleanExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return example;

        // Remove Chinese characters
        var cleaned = CollinsExtractor.RemoveChineseCharacters(example);

        // Clean up
        cleaned = cleaned.Replace("  ", " ")
                        .Replace(".,.", ".")
                        .Replace("...", ".")
                        .Replace("·", "")
                        .Trim();

        // Remove any bracket content
        cleaned = Regex.Replace(cleaned, @"【[^】]*】", "");

        // Ensure proper ending
        if (!string.IsNullOrEmpty(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private static string BuildRawFragment(CollinsSenseRaw sense)
    {
        // Use raw text if available, otherwise construct
        if (!string.IsNullOrEmpty(sense.RawText))
            return sense.RawText;

        var parts = new List<string>();

        // Add sense header
        if (sense.SenseNumber > 0)
        {
            var pos = !string.IsNullOrEmpty(sense.PartOfSpeech) ? sense.PartOfSpeech : "UNK";
            parts.Add($"{sense.SenseNumber}.{pos}");
        }

        // Add definition
        if (!string.IsNullOrEmpty(sense.Definition))
        {
            parts.Add(sense.Definition);
        }

        // Add examples if any
        if (sense.Examples?.Any() == true)
        {
            parts.AddRange(sense.Examples.Select(e => $"• {e}"));
        }

        return string.Join("\n", parts).Trim();
    }

    private static string CleanUsageNote(string usageNote)
    {
        if (string.IsNullOrWhiteSpace(usageNote))
            return usageNote;

        return CollinsExtractor.RemoveChineseCharacters(usageNote)
            .Replace("  ", " ")
            .Trim();
    }

    private static string CleanDomainLabel(string domainLabel)
    {
        if (string.IsNullOrWhiteSpace(domainLabel))
            return domainLabel;

        // Normalize domain labels
        var cleaned = CollinsExtractor.RemoveChineseCharacters(domainLabel)
            .Replace("  ", " ")
            .Trim();

        if (cleaned.Equals("BRIT", StringComparison.OrdinalIgnoreCase))
            return "UK";
        if (cleaned.Equals("AM", StringComparison.OrdinalIgnoreCase))
            return "US";
        if (cleaned.Contains("主英") || cleaned.Contains("英式"))
            return "UK";
        if (cleaned.Contains("主美") || cleaned.Contains("美式"))
            return "US";
        if (cleaned.Contains("正式"))
            return "FORMAL";
        if (cleaned.Contains("非正式"))
            return "INFORMAL";

        return cleaned;
    }

    private static string CleanGrammarInfo(string grammarInfo)
    {
        if (string.IsNullOrWhiteSpace(grammarInfo))
            return grammarInfo;

        return CollinsExtractor.RemoveChineseCharacters(grammarInfo)
            .Replace("  ", " ")
            .Trim();
    }

    private static string ExtractIPA(string headword)
    {
        if (string.IsNullOrWhiteSpace(headword))
            return null;

        // Look for IPA in slashes: /ɪnˈtɛlɪdʒəns/
        var match = Regex.Match(headword, @"/([^/]+)/");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return null;
    }
}