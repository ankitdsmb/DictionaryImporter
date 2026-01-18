using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using JsonException = Newtonsoft.Json.JsonException;

namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiExtractor(ILogger<KaikkiExtractor> logger)
        : IDataExtractor<KaikkiRawEntry>
    {
        // Bigger buffer helps on large sequential reads (JSONL)
        private const int BufferSize = 1024 * 1024; // 1MB

        // Log progress every N lines
        private const int ProgressEvery = 100_000;

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            // If a field type mismatches, don't crash entire import
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal
        };

        public async IAsyncEnumerable<KaikkiRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            logger.LogInformation("Kaikki.org extraction started");

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: BufferSize,
                leaveOpen: true);

            long lineNumber = 0;
            long yielded = 0;

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                lineNumber++;

                string? line;
                try
                {
                    line = await reader.ReadLineAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to read line {LineNumber}", lineNumber);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                KaikkiRawEntry? entry;
                try
                {
                    entry = JsonConvert.DeserializeObject<KaikkiRawEntry>(line, JsonSettings);
                }
                catch (JsonReaderException ex)
                {
                    // This is common in huge corpuses: skip bad line, continue
                    logger.LogWarning(ex, "Invalid JSON at line {LineNumber}", lineNumber);
                    continue;
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "JSON deserialize error at line {LineNumber}", lineNumber);
                    continue;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error parsing line {LineNumber}", lineNumber);
                    continue;
                }

                if (entry == null)
                    continue;

                // Word is mandatory
                if (string.IsNullOrWhiteSpace(entry.Word))
                    continue;

                // Only English (Kaikki uses "en" for English code)
                if (!string.Equals(entry.LanguageCode, "English", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Normalize: if IPA list exists but Sounds missing, merge them
                MergeLegacyIpaIntoSounds(entry);

                yielded++;
                yield return entry;

                if (lineNumber % ProgressEvery == 0)
                {
                    logger.LogInformation(
                        "Kaikki.org progress: lines={Lines:n0}, yielded={Yielded:n0}",
                        lineNumber,
                        yielded);
                }
            }

            logger.LogInformation(
                "Kaikki.org extraction completed. lines={Lines:n0}, yielded={Yielded:n0}",
                lineNumber,
                yielded);
        }

        private static void MergeLegacyIpaIntoSounds(KaikkiRawEntry entry)
        {
            if (entry.Ipa == null || entry.Ipa.Count == 0)
                return;

            entry.Sounds ??= new List<KaikkiSound>();

            foreach (var ipa in entry.Ipa)
            {
                if (string.IsNullOrWhiteSpace(ipa))
                    continue;

                entry.Sounds.Add(new KaikkiSound
                {
                    Ipa = ipa.Trim(),
                    Tags = new List<string> { "general" }
                });
            }
        }
    }
}