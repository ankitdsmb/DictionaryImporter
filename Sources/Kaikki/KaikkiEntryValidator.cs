using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Sources.Kaikki;

public sealed class KaikkiEntryValidator(ILogger<KaikkiEntryValidator> logger) : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        // If RawFragment exists, use it as the source of truth
        if (!string.IsNullOrWhiteSpace(entry.RawFragment))
        {
            var isEnglish =
                entry.RawFragment.Contains("\"lang_code\":\"en\"", StringComparison.OrdinalIgnoreCase) ||
                entry.RawFragment.Contains("\"lang_code\": \"en\"", StringComparison.OrdinalIgnoreCase);

            if (isEnglish)
                return ValidationResult.Valid();

            logger.LogDebug("Skipping non-English Kaikki entry: {Word}", entry.Word);
            return ValidationResult.Invalid("Not an English dictionary entry");
        }

        // Fallback only when raw fragment is missing
        if (!string.IsNullOrWhiteSpace(entry.Definition) && entry.Definition.Length > 5)
            return ValidationResult.Valid();

        logger.LogDebug("Skipping Kaikki entry due to missing content: {Word}", entry.Word);
        return ValidationResult.Invalid("Insufficient entry content");
    }
}