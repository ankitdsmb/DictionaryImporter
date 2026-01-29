// File: Infrastructure/Parsing/DefaultDictionaryTextFormatter.cs

using System;
using System.Net;
using System.Text.RegularExpressions;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Core.Text;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class DefaultDictionaryTextFormatter(ILogger<DefaultDictionaryTextFormatter> logger)
    : IDictionaryTextFormatter
{
    private readonly ILogger<DefaultDictionaryTextFormatter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public string FormatDefinition(string definition)
    {
        return definition?.Trim() ?? string.Empty;
    }

    public string FormatExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var formatted = NormalizeSpacing(example);

        if (!formatted.EndsWith(".") && !formatted.EndsWith("!") && !formatted.EndsWith("?"))
            formatted += ".";

        return formatted;
    }

    public string FormatSynonym(string synonym)
    {
        if (string.IsNullOrWhiteSpace(synonym))
            return string.Empty;

        return NormalizeSpacing(synonym).ToLowerInvariant();
    }

    public string FormatAntonym(string antonym)
    {
        if (string.IsNullOrWhiteSpace(antonym))
            return string.Empty;

        return NormalizeSpacing(antonym).ToLowerInvariant();
    }

    public string FormatEtymology(string etymology)
    {
        return etymology?.Trim() ?? string.Empty;
    }

    public string FormatNote(string note)
    {
        return note?.Trim() ?? string.Empty;
    }

    public string FormatDomain(string domain)
    {
        return domain?.Trim() ?? string.Empty;
    }

    public string FormatUsageLabel(string usageLabel)
    {
        return usageLabel?.Trim() ?? string.Empty;
    }

    public string FormatCrossReference(CrossReference crossReference)
    {
        if (crossReference == null)
            return string.Empty;

        var target = (crossReference.TargetWord ?? string.Empty).Trim();
        var type = (crossReference.ReferenceType ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(target))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(type))
            return target;

        return $"{target} ({type})";
    }

    public string CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        try
        {
            // Remove tags (simple + safe for DI)
            var cleaned = Regex.Replace(html, "<.*?>", string.Empty);
            cleaned = WebUtility.HtmlDecode(cleaned);

            return NormalizeSpacing(cleaned);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CleanHtml failed.");
            return html.Trim();
        }
    }

    public string NormalizeSpacing(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    public string EnsureProperPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = NormalizeSpacing(text);

        if (!trimmed.EndsWith(".") &&
            !trimmed.EndsWith("!") &&
            !trimmed.EndsWith("?") &&
            !trimmed.EndsWith(":") &&
            !trimmed.EndsWith(";") &&
            !trimmed.EndsWith(","))
        {
            if (trimmed.Length > 0 && char.IsLetter(trimmed[0]) && trimmed.Contains(' '))
                trimmed += ".";
        }

        return trimmed;
    }

    public string RemoveFormattingMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = text;

        var markers = new[]
        {
            "★", "☆", "●", "○", "▶",
            "【", "】", "〖", "〗",
            "《", "》", "〈", "〉"
        };

        foreach (var marker in markers)
            result = result.Replace(marker, string.Empty);

        return NormalizeSpacing(result);
    }
}