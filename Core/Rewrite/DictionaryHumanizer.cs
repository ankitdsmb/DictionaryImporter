using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Humanizer;

namespace DictionaryImporter.Core.Rewrite;

public sealed class DictionaryHumanizer
{
    private static readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    // Configuration for humanization
    private readonly HumanizerConfig _config;

    public DictionaryHumanizer(HumanizerConfig? config = null)
    {
        _config = config ?? new HumanizerConfig();
    }

    public class HumanizerConfig
    {
        public bool UseSentenceCase { get; set; } = true;
        public bool NormalizeNumbers { get; set; } = true;
        public bool HumanizeDates { get; set; } = true;
        public bool HumanizeTimespans { get; set; } = true;
        public bool TitleCaseHeadings { get; set; } = false;
        public bool ConvertNumbersToWords { get; set; } = false;
        public bool FixCommonTypos { get; set; } = true;
        public bool ExpandAbbreviations { get; set; } = false;
        public string LanguageCode { get; set; } = "en";
        public bool PreserveFormattingMarkers { get; set; } = true;
    }

    public string HumanizeDefinition(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var originalText = text;

        try
        {
            text = text.Trim();

            // Phase 1: Apply Humanizr transformations
            text = ApplyHumanizrTransforms(text, HumanizationContext.Definition);

            // Phase 2: Normalize whitespace and punctuation (preserving Humanizr changes)
            text = NormalizeWhitespace(text);
            text = NormalizePunctuationSpacing(text);

            // Phase 3: Apply context-specific casing
            if (_config.UseSentenceCase && ShouldApplySentenceCase(text))
            {
                text = ApplyContextAwareCasing(text, HumanizationContext.Definition);
            }

            // Phase 4: Final punctuation normalization
            text = NormalizeEndingPunctuation(text);

            // Phase 5: Ensure we didn't break the text
            text = ValidateAndFix(text, originalText);

            return text.Trim();
        }
        catch
        {
            // Fallback to basic normalization
            return FallbackNormalize(originalText);
        }
    }

    public string HumanizeExample(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var originalText = text;

        try
        {
            text = text.Trim();

            // Apply Humanizr with example-specific rules
            text = ApplyHumanizrTransforms(text, HumanizationContext.Example);

            text = NormalizeWhitespace(text);
            text = NormalizePunctuationSpacing(text);

            if (_config.UseSentenceCase && ShouldApplySentenceCase(text))
            {
                text = ApplyContextAwareCasing(text, HumanizationContext.Example);
            }

            text = NormalizeEndingPunctuation(text);
            text = ValidateAndFix(text, originalText);

            return text.Trim();
        }
        catch
        {
            return FallbackNormalize(originalText);
        }
    }

    public string HumanizeTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var originalText = text;

        try
        {
            text = text.Trim();

            // Apply Humanizr with title-specific rules (more conservative)
            text = ApplyHumanizrTransforms(text, HumanizationContext.Title);

            text = NormalizeWhitespace(text);
            text = NormalizePunctuationSpacing(text);

            // Optionally apply title case if configured
            if (_config.TitleCaseHeadings)
            {
                text = ApplyTitleCase(text);
            }

            text = ValidateAndFix(text, originalText);

            return text.Trim();
        }
        catch
        {
            return FallbackNormalize(originalText);
        }
    }

    // NEW: Apply Humanizr transformations based on context
    private string ApplyHumanizrTransforms(string text, HumanizationContext context)
    {
        var result = text;

        // 1. Fix common typos using Humanizr's ToQuantity and transformations
        if (_config.FixCommonTypos)
        {
            result = FixCommonTypos(result);
        }

        // 2. Normalize numbers (convert "1st" to "first", etc.)
        if (_config.NormalizeNumbers && context != HumanizationContext.Title)
        {
            result = NormalizeNumbers(result);
        }

        // 3. Convert numbers to words if configured
        if (_config.ConvertNumbersToWords && context == HumanizationContext.Example)
        {
            result = ConvertNumbersToWords(result);
        }

        // 4. Humanize dates and times
        if (_config.HumanizeDates)
        {
            result = HumanizeDates(result);
        }

        if (_config.HumanizeTimespans)
        {
            result = HumanizeTimespans(result);
        }

        // 5. Expand abbreviations if configured
        if (_config.ExpandAbbreviations && context == HumanizationContext.Definition)
        {
            result = ExpandCommonAbbreviations(result);
        }

        return result;
    }

    // NEW: Fix common typos using Humanizr patterns
    private string FixCommonTypos(string text)
    {
        var result = text;

        // Common number typos
        result = Regex.Replace(result, @"\b(\d+)(?:st|nd|rd|th)\b", match =>
        {
            if (int.TryParse(match.Groups[1].Value, out int number))
            {
                try
                {
                    return number.ToOrdinalWords();
                }
                catch
                {
                    return match.Value;
                }
            }
            return match.Value;
        });

        // Fix common word typos using Humanizr's Dehumanize/Humanize
        var commonTypos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["teh"] = "the",
            ["adn"] = "and",
            ["waht"] = "what",
            ["dont"] = "don't",
            ["cant"] = "can't",
            ["wont"] = "won't",
            ["its"] = "it's",
            ["your"] = "you're",
            ["there"] = "their",
            ["they're"] = "their",
            ["loose"] = "lose",
            ["effect"] = "affect",
            ["accept"] = "except"
        };

        foreach (var typo in commonTypos)
        {
            result = Regex.Replace(result, $@"\b{Regex.Escape(typo.Key)}\b", typo.Value, RegexOptions.IgnoreCase);
        }

        return result;
    }

    // NEW: Normalize numbers using Humanizr
    private string NormalizeNumbers(string text)
    {
        var result = text;

        // Convert "1st", "2nd", "3rd", "4th" to ordinal words
        result = Regex.Replace(result, @"\b(\d+)(?:st|nd|rd|th)\b", match =>
        {
            if (int.TryParse(match.Groups[1].Value, out int number))
            {
                try
                {
                    // Use Humanizr for ordinal words
                    return number.ToOrdinalWords();
                }
                catch
                {
                    return match.Value;
                }
            }
            return match.Value;
        });

        // Convert Roman numerals to numbers where appropriate
        result = Regex.Replace(result, @"\b([IVXLCDM]+)\b", match =>
        {
            var roman = match.Value;
            try
            {
                // Humanizr doesn't have Roman numeral conversion, so we'll do it manually
                var number = RomanToInteger(roman);
                if (number > 0 && number < 100)
                {
                    return number.ToWords();
                }
            }
            catch
            {
                // Keep Roman numeral if conversion fails
            }
            return roman;
        }, RegexOptions.IgnoreCase);

        return result;
    }

    // NEW: Convert numbers to words using Humanizr
    private string ConvertNumbersToWords(string text)
    {
        var result = text;

        // Convert standalone numbers to words (1-1000)
        result = Regex.Replace(result, @"\b(\d{1,3})\b", match =>
        {
            if (int.TryParse(match.Value, out int number))
            {
                if (number >= 1 && number <= 1000)
                {
                    try
                    {
                        return number.ToWords();
                    }
                    catch
                    {
                        return match.Value;
                    }
                }
            }
            return match.Value;
        });

        // Handle fractions
        result = Regex.Replace(result, @"\b(\d+)/(\d+)\b", match =>
        {
            if (int.TryParse(match.Groups[1].Value, out int numerator) &&
                int.TryParse(match.Groups[2].Value, out int denominator))
            {
                if (denominator > 0 && denominator <= 10)
                {
                    try
                    {
                        if (numerator == 1)
                        {
                            return denominator.ToOrdinalWords().ToLower() + (denominator == 4 ? "th" : "");
                        }
                        else
                        {
                            return $"{numerator.ToWords()} {denominator.ToOrdinalWords().ToLower() + "s"}";
                        }
                    }
                    catch
                    {
                        return match.Value;
                    }
                }
            }
            return match.Value;
        });

        return result;
    }

    // NEW: Humanize dates using Humanizr
    private string HumanizeDates(string text)
    {
        var result = text;

        // Convert common date formats to human-readable
        var datePatterns = new[]
        {
            @"\b(\d{1,2})/(\d{1,2})/(\d{2,4})\b",
            @"\b(\d{2,4})-(\d{1,2})-(\d{1,2})\b"
        };

        foreach (var pattern in datePatterns)
        {
            result = Regex.Replace(result, pattern, match =>
            {
                if (DateTime.TryParse(match.Value, out DateTime date))
                {
                    try
                    {
                        // Humanizr's Humanize for dates
                        var now = DateTime.Now;
                        var difference = now - date;

                        if (Math.Abs(difference.TotalDays) < 60)
                        {
                            return date.Humanize();
                        }
                        else
                        {
                            return date.ToString("MMMM d, yyyy", _culture);
                        }
                    }
                    catch
                    {
                        return match.Value;
                    }
                }
                return match.Value;
            });
        }

        return result;
    }

    // NEW: Humanize timespans using Humanizr
    private string HumanizeTimespans(string text)
    {
        var result = text;

        // Convert time durations to human-readable
        result = Regex.Replace(result, @"\b(\d+)\s*(seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?|months?|years?)\b",
            match =>
            {
                var number = match.Groups[1].Value;
                var unit = match.Groups[2].Value.ToLower();

                if (int.TryParse(number, out int value))
                {
                    try
                    {
                        TimeSpan? timeSpan = null;

                        switch (unit)
                        {
                            case "second":
                            case "seconds":
                            case "sec":
                            case "secs":
                                timeSpan = TimeSpan.FromSeconds(value);
                                break;

                            case "minute":
                            case "minutes":
                            case "min":
                            case "mins":
                                timeSpan = TimeSpan.FromMinutes(value);
                                break;

                            case "hour":
                            case "hours":
                            case "hr":
                            case "hrs":
                                timeSpan = TimeSpan.FromHours(value);
                                break;

                            case "day":
                            case "days":
                                timeSpan = TimeSpan.FromDays(value);
                                break;

                            case "week":
                            case "weeks":
                                timeSpan = TimeSpan.FromDays(value * 7);
                                break;
                        }

                        if (timeSpan.HasValue)
                        {
                            return timeSpan.Value.Humanize(maxUnit: TimeUnit.Year);
                        }
                    }
                    catch
                    {
                        return match.Value;
                    }
                }

                return match.Value;
            }, RegexOptions.IgnoreCase);

        return result;
    }

    // NEW: Expand common abbreviations
    private string ExpandCommonAbbreviations(string text)
    {
        if (!_config.ExpandAbbreviations) return text;

        var result = text;

        var abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["e.g."] = "for example",
            ["i.e."] = "that is",
            ["etc."] = "and so on",
            ["vs."] = "versus",
            ["viz."] = "namely",
            ["cf."] = "compare",
            ["et al."] = "and others",
            ["approx."] = "approximately",
            ["dept."] = "department",
            ["univ."] = "university",
            ["assoc."] = "association",
            ["prof."] = "professor",
            ["dr."] = "doctor",
            ["mr."] = "mister",
            ["mrs."] = "missus",
            ["ms."] = "miss"
        };

        foreach (var abbr in abbreviations)
        {
            result = Regex.Replace(result, $@"\b{Regex.Escape(abbr.Key)}\b", abbr.Value, RegexOptions.IgnoreCase);
        }

        return result;
    }

    // NEW: Apply context-aware casing
    private string ApplyContextAwareCasing(string text, HumanizationContext context)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Check if we should apply casing
        if (!ShouldApplySentenceCase(text))
            return text;

        // Use Humanizr's Transform method or custom logic
        var idx = FirstLetterIndex(text);
        if (idx < 0)
            return text;

        // Apply sentence case using Humanizr's casing methods
        var firstPart = text.Substring(0, idx);
        var firstLetter = text[idx];
        var rest = text.Substring(idx + 1);

        // Check if already uppercase
        if (char.IsUpper(firstLetter))
            return text;

        // Use proper casing based on context
        var upperFirst = char.ToUpperInvariant(firstLetter);

        // For titles, consider using Title Case
        if (context == HumanizationContext.Title && _config.TitleCaseHeadings)
        {
            return ApplyTitleCase(text);
        }

        return firstPart + upperFirst + rest;
    }

    // NEW: Apply title case using Humanizr
    private string ApplyTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Use Humanizr's Transform or implement title case
        // Since Humanizr doesn't have direct TitleCase, we'll implement it
        var words = text.Split(' ');
        var result = new StringBuilder();

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];

            // Skip small words in the middle (a, an, the, and, but, or, etc.)
            if (i > 0 && i < words.Length - 1 && IsSmallWord(word))
            {
                result.Append(word.ToLower());
            }
            else
            {
                // Capitalize first letter
                if (word.Length > 0)
                {
                    result.Append(char.ToUpper(word[0]));
                    if (word.Length > 1)
                    {
                        result.Append(word.Substring(1).ToLower());
                    }
                }
            }

            if (i < words.Length - 1)
            {
                result.Append(' ');
            }
        }

        return result.ToString();
    }

    private bool IsSmallWord(string word)
    {
        var smallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "and", "but", "or", "nor", "for", "so", "yet",
            "at", "by", "in", "of", "on", "to", "up", "as", "is", "it"
        };

        return smallWords.Contains(word.TrimEnd('.', ',', ';', ':'));
    }

    // NEW: Validate and fix text after transformations
    private string ValidateAndFix(string transformed, string original)
    {
        // Basic validation to ensure we didn't break the text
        if (string.IsNullOrWhiteSpace(transformed))
            return original;

        // Ensure transformations didn't create nonsense
        if (transformed.Length < original.Length / 2 && original.Length > 10)
            return original; // Too much was removed

        // Check for common issues
        if (transformed.Contains("  ") || transformed.StartsWith(" ") || transformed.EndsWith(" "))
        {
            transformed = transformed.Trim();
            transformed = Regex.Replace(transformed, @"\s{2,}", " ");
        }

        return transformed;
    }

    // NEW: Roman numeral converter helper
    private int RomanToInteger(string roman)
    {
        var values = new Dictionary<char, int>
        {
            ['I'] = 1,
            ['V'] = 5,
            ['X'] = 10,
            ['L'] = 50,
            ['C'] = 100,
            ['D'] = 500,
            ['M'] = 1000
        };

        roman = roman.ToUpper();
        int total = 0;
        int prevValue = 0;

        for (int i = roman.Length - 1; i >= 0; i--)
        {
            if (!values.TryGetValue(roman[i], out int value))
                return 0;

            if (value < prevValue)
                total -= value;
            else
                total += value;

            prevValue = value;
        }

        return total;
    }

    // Original methods (unchanged signature, enhanced implementation)
    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Use more robust whitespace normalization
        text = text.Replace("\t", " ")
                   .Replace("\r\n", "\n")
                   .Replace("\r", "\n");

        text = Regex.Replace(text, @"\s{2,}", " ");
        text = Regex.Replace(text, @"\s+\n", "\n");
        text = Regex.Replace(text, @"\n\s+", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n"); // Limit consecutive newlines

        return text.Trim();
    }

    private static string NormalizePunctuationSpacing(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove space before punctuation.
        text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");

        // Ensure one space after punctuation when followed by letter/number
        text = Regex.Replace(text, @"([,.;:!?])([A-Za-z0-9])", "$1 $2");

        // Fix multiple punctuation
        text = Regex.Replace(text, @"(?<!\.)\.\.(?!\.)", ".");
        text = Regex.Replace(text, @"!{2,}", "!");
        text = Regex.Replace(text, @"\?{2,}", "?");

        // Fix spaces inside quotes
        text = Regex.Replace(text, @"\s+""", "\"");
        text = Regex.Replace(text, @"""\s+", "\"");

        return text.Trim();
    }

    private static bool ShouldApplySentenceCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Enhanced checks with Humanizr awareness
        if (text.Contains(" /") && text.Contains("/ "))
            return false;

        if (Regex.IsMatch(text, @"\[[^\]]+\]"))
            return false;

        if (Regex.IsMatch(text, @"\b[A-Z]{3,}\b"))
            return false;

        // Check if text contains transformed numbers (might be words now)
        if (Regex.IsMatch(text, @"\b(one|two|three|four|five|six|seven|eight|nine|ten)\b", RegexOptions.IgnoreCase))
            return true; // These are fine

        var firstLetterIndex = FirstLetterIndex(text);
        return firstLetterIndex >= 0;
    }

    private static int FirstLetterIndex(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsLetter(ch))
                return i;
        }
        return -1;
    }

    private static string NormalizeEndingPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.Trim();

        var endingChars = new[] { "\"", "'", ")", "]", "}", ":", "—", "–" };
        foreach (var ending in endingChars)
        {
            if (text.EndsWith(ending, StringComparison.Ordinal))
                return text;
        }

        if (text.EndsWith(";", StringComparison.Ordinal) || text.EndsWith(",", StringComparison.Ordinal))
        {
            return text.Substring(0, text.Length - 1).TrimEnd() + ".";
        }

        // Add period if it looks like a sentence but has no ending punctuation
        if (!text.EndsWith(".") && !text.EndsWith("!") && !text.EndsWith("?") &&
            text.Length > 10 && char.IsLetter(text[^1]))
        {
            return text + ".";
        }

        return text;
    }

    private string FallbackNormalize(string text)
    {
        // Simple fallback without Humanizr
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = NormalizeWhitespace(text);
        text = NormalizePunctuationSpacing(text);

        if (ShouldApplySentenceCase(text))
        {
            var idx = FirstLetterIndex(text);
            if (idx >= 0)
            {
                var ch = text[idx];
                if (char.IsLower(ch))
                {
                    text = text.Substring(0, idx) + char.ToUpper(ch) + text.Substring(idx + 1);
                }
            }
        }

        text = NormalizeEndingPunctuation(text);

        return text.Trim();
    }

    // Helper enum for context
    private enum HumanizationContext
    {
        Definition,
        Example,
        Title
    }
}