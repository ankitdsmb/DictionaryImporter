using System;
using System.Linq;
using DictionaryImporter.Infrastructure.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseEntryValidator : IDictionaryEntryValidator
{
    private readonly ILogger<EnglishChineseEntryValidator> _logger;

    // ✅ Default constructor (no DI required)
    public EnglishChineseEntryValidator()
    {
        _logger = NullLogger<EnglishChineseEntryValidator>.Instance;
    }

    // ✅ DI constructor
    public EnglishChineseEntryValidator(ILogger<EnglishChineseEntryValidator> logger)
    {
        _logger = logger ?? NullLogger<EnglishChineseEntryValidator>.Instance;
    }

    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (entry == null)
            return ValidationResult.Invalid("Entry is null");

        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition) && string.IsNullOrWhiteSpace(entry.RawFragment))
            return ValidationResult.Invalid("No content for ENG_CHN entry");

        var content = entry.RawFragment ?? entry.Definition ?? string.Empty;

        if (!content.Contains('⬄') && !ContainsChineseCharacters(content))
        {
            _logger.LogDebug("ENG_CHN entry missing ⬄ separator and Chinese: {Word}", entry.Word);
            return ValidationResult.Invalid("Not a valid ENG_CHN entry");
        }

        return ValidationResult.Valid();
    }

    private static bool ContainsChineseCharacters(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Any(c => c >= 0x4E00 && c <= 0x9FFF);
    }
}