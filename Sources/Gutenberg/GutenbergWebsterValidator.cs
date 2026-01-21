using DictionaryImporter.Sources.Kaikki;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Sources.Gutenberg
{
    public class GutenbergWebsterValidator(ILogger<GutenbergWebsterValidator> logger) : IDictionaryEntryValidator
    {
        private readonly ILogger<GutenbergWebsterValidator> _logger = logger;

        public ValidationResult Validate(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                return ValidationResult.Invalid("Word missing");

            // Fallback only when raw fragment is missing
            if (!string.IsNullOrWhiteSpace(entry.Definition) && entry.Definition.Length > 5)
                return ValidationResult.Valid();

            return ValidationResult.Invalid("Insufficient entry content");
        }
    }
}