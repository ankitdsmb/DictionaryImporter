using JsonException = Newtonsoft.Json.JsonException;

namespace DictionaryImporter.Sources.Kaikki;

public sealed class KaikkiExtractor(ILogger<KaikkiExtractor> logger) : IDataExtractor<KaikkiRawEntry>
{
    private const int BufferSize = 8192;

    public async IAsyncEnumerable<KaikkiRawEntry> ExtractAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        logger.LogInformation("Kaikki.org extraction started");

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
                    logger.LogDebug("Failed to deserialize line {LineNumber}", lineNumber);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Word))
                {
                    logger.LogDebug("Skipping entry with empty word at line {LineNumber}", lineNumber);
                    continue;
                }

                if (!string.Equals(entry.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.Ipa != null && entry.Ipa.Count > 0)
                {
                    if (entry.Sounds == null)
                        entry.Sounds = [];

                    foreach (var ipa in entry.Ipa)
                    {
                        if (!string.IsNullOrWhiteSpace(ipa))
                        {
                            entry.Sounds.Add(new KaikkiSound
                            {
                                Ipa = ipa,
                                Tags = ["general"]
                            });
                        }
                    }
                }

                isValid = true;
            }
            catch (JsonException jsonEx)
            {
                logger.LogWarning(jsonEx, "JSON parsing error at line {LineNumber}", lineNumber);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing line {LineNumber}", lineNumber);
            }

            if (isValid && entry != null)
            {
                yield return entry;

                if (lineNumber % 10000 == 0)
                {
                    logger.LogInformation("Kaikki.org progress: {Count} entries processed", lineNumber);
                }
            }
        }

        logger.LogInformation("Kaikki.org extraction completed. Total entries: {Count}", lineNumber);
    }
}