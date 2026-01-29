using System.Text.RegularExpressions;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Engines;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.Core.Text;

public sealed class OcrArtifactNormalizer(
    IOptions<OcrNormalizationOptions> options,
    ILogger<OcrArtifactNormalizer> logger)
    : IOcrArtifactNormalizer
{
    private static readonly Regex TokenRegex =
        new(@"\b[a-z]{6,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MultiSpace =
        new(@"\s{2,}", RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuation =
        new(@"\s+([,.;:!?])", RegexOptions.Compiled);

    private static readonly Regex MissingSpaceAfterPunctuation =
        new(@"([,.;:!?])([A-Za-z])", RegexOptions.Compiled);

    private static readonly Regex SpaceAfterOpenBracket =
        new(@"([(\[])\s+", RegexOptions.Compiled);

    private static readonly Regex SpaceBeforeCloseBracket =
        new(@"\s+([)\]])", RegexOptions.Compiled);

    private readonly OcrNormalizationOptions _options = options.Value ?? new OcrNormalizationOptions();

    public string Normalize(string text, string languageCode = "en")
    {
        if (!_options.Enabled)
            return text;

        if (string.IsNullOrWhiteSpace(text))
            return text;

        var original = text;

        // 1) Normalize new lines and tabs
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        text = text.Replace("\t", " ");

        // 2) Normalize quotes
        text = text
            .Replace("“", "\"")
            .Replace("”", "\"")
            .Replace("’", "'");

        // 3) Apply config replacements (case-insensitive, whole word)
        if (_options.EnableReplacements && _options.Replacements is { Count: > 0 })
        {
            foreach (var kv in _options.Replacements)
            {
                var from = kv.Key?.Trim();
                var to = kv.Value ?? "";

                if (string.IsNullOrWhiteSpace(from))
                    continue;

                var pattern = new Regex($@"\b{Regex.Escape(from)}\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);

                text = pattern.Replace(text, to);
            }
        }

        // 4) Fix punctuation spacing
        text = SpaceBeforePunctuation.Replace(text, "$1");
        text = MissingSpaceAfterPunctuation.Replace(text, "$1 $2");

        // 5) Fix bracket spacing
        text = SpaceAfterOpenBracket.Replace(text, "$1");
        text = SpaceBeforeCloseBracket.Replace(text, "$1");

        // 6) Collapse multiple spaces
        text = MultiSpace.Replace(text, " ");

        // 7) Trim each line
        var lines = text.Split('\n').Select(x => x.Trim());
        text = string.Join("\n", lines).Trim();

        // 8) Hunspell splitting (optional)
        if (_options.EnableHunspellSplit)
        {
            text = SplitJoinedWordsUsingHunspell(text, languageCode);
        }

        // Final cleanup
        text = MultiSpace.Replace(text, " ").Trim();

        if (_options.LogChanges && !string.Equals(original, text, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "OCR normalization applied | Before='{Before}' | After='{After}'",
                Truncate(original, 200),
                Truncate(text, 200));
        }

        return text;
    }

    private string SplitJoinedWordsUsingHunspell(string text, string languageCode)
    {
        var spellChecker = new NHunspellSpellChecker(languageCode);

        if (!spellChecker.IsSupported)
            return text;

        var minLen = _options.MinTokenLengthForSplit <= 0
            ? 6
            : _options.MinTokenLengthForSplit;

        return TokenRegex.Replace(text, match =>
        {
            var token = match.Value;

            if (token.Length < minLen)
                return token;

            // correct => keep
            if (spellChecker.Check(token).IsCorrect)
                return token;

            // try split into 2 valid words
            var split = TrySplitIntoTwoWords(token, spellChecker);
            return split ?? token;
        });
    }

    private static string? TrySplitIntoTwoWords(string token, ISpellChecker spellChecker)
    {
        for (int i = 2; i <= token.Length - 2; i++)
        {
            var left = token[..i];
            var right = token[i..];

            if (!spellChecker.Check(left).IsCorrect)
                continue;

            if (!spellChecker.Check(right).IsCorrect)
                continue;

            return left + " " + right;
        }

        return null;
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length <= maxLen)
            return value;

        return value.Substring(0, maxLen) + "...";
    }
}