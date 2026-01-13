using DictionaryImporter.Sources.Kaikki.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DictionaryImporter.Sources.Kaikki;

public sealed class KaikkiExtractor : IDataExtractor<KaikkiRawEntry>
{
    private readonly ILogger<KaikkiExtractor> _logger;
    private const int BufferSize = 8192;

    public KaikkiExtractor(ILogger<KaikkiExtractor> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<KaikkiRawEntry> ExtractAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Kaikki.org extraction started");

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, BufferSize, true);

        long lineNumber = 0;
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            KaikkiRawEntry? entry = null;
            bool isValid = false;

            try
            {
                entry = JsonConvert.DeserializeObject<KaikkiRawEntry>(line);
                if (entry == null)
                {
                    _logger.LogDebug("Failed to deserialize line {LineNumber}", lineNumber);
                    continue;
                }

                // Validate the entry
                if (string.IsNullOrWhiteSpace(entry.Word))
                {
                    _logger.LogDebug("Skipping entry with empty word at line {LineNumber}", lineNumber);
                    continue;
                }

                // Filter for English entries only (or skip if language not specified)
                if (!string.Equals(entry.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                {
                    // Optionally skip non-English entries
                    continue;
                }

                // Add IPA from ipa field if sounds doesn't have it
                if (entry.Ipa != null && entry.Ipa.Count > 0)
                {
                    if (entry.Sounds == null)
                        entry.Sounds = new List<KaikkiSound>();

                    foreach (var ipa in entry.Ipa)
                    {
                        if (!string.IsNullOrWhiteSpace(ipa))
                        {
                            entry.Sounds.Add(new KaikkiSound
                            {
                                Ipa = ipa,
                                Tags = new List<string> { "general" }
                            });
                        }
                    }
                }

                isValid = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "JSON parsing error at line {LineNumber}", lineNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing line {LineNumber}", lineNumber);
            }

            if (isValid && entry != null)
            {
                yield return entry;

                if (lineNumber % 10000 == 0)
                {
                    _logger.LogInformation("Kaikki.org progress: {Count} entries processed", lineNumber);
                }
            }
        }

        _logger.LogInformation("Kaikki.org extraction completed. Total entries: {Count}", lineNumber);
    }
}