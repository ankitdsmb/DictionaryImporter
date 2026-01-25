namespace DictionaryImporter.Core.Abstractions;

// FIXED: Complete IDictionaryTextFormatter implementation
public interface IDictionaryTextFormatter
{
    string FormatDefinition(string definition);
    string FormatExample(string example);
    string FormatSynonym(string synonym);
    string FormatAntonym(string antonym); // Added missing method
    string FormatEtymology(string etymology); // Added missing method
    string FormatNote(string note);
    string FormatDomain(string domain);
    string FormatUsageLabel(string usageLabel);
    string FormatCrossReference(CrossReference crossReference);
    string CleanHtml(string html);
    string NormalizeSpacing(string text);
    string EnsureProperPunctuation(string text);
    string RemoveFormattingMarkers(string text);
}