namespace DictionaryImporter.Sources.Kaikki.Validation
{
    public sealed class KaikkiEntryValidator : IDictionaryEntryValidator
    {
        private readonly ILogger<KaikkiEntryValidator> _logger;

        public KaikkiEntryValidator(ILogger<KaikkiEntryValidator> logger)
        {
            _logger = logger;
        }

        public ValidationResult Validate(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                return ValidationResult.Invalid("Word missing");

            // For Kaikki, we need to check if it's actually an English dictionary entry
            if (!string.IsNullOrWhiteSpace(entry.RawFragment) &&
                entry.RawFragment.Contains("\"lang_code\":\"en\""))
            {
                // This is an English entry
                return ValidationResult.Valid();
            }

            // Check if it has English definitions
            if (!string.IsNullOrWhiteSpace(entry.Definition) &&
                entry.Definition.Length > 5)
            {
                return ValidationResult.Valid();
            }

            _logger.LogDebug("Skipping non-English Kaikki entry: {Word}", entry.Word);
            return ValidationResult.Invalid("Not an English dictionary entry");
        }
    }
}