namespace DictionaryImporter.Gateway.Rewriter;

internal static class LuceneMemorySuggestionJsonWriter
{
    public static string UpsertLuceneSuggestions(string? aiNotesJson, IReadOnlyList<LuceneSuggestionResult> suggestions)
    {
        if (suggestions == null || suggestions.Count == 0)
            return aiNotesJson ?? "{}";

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(aiNotesJson) ? "{}" : aiNotesJson);
            var root = doc.RootElement;

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

            writer.WriteStartObject();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("luceneSuggestions"))
                    continue;

                prop.WriteTo(writer);
            }

            writer.WritePropertyName("luceneSuggestions");
            writer.WriteStartArray();

            foreach (var s in suggestions)
            {
                writer.WriteStartObject();
                writer.WriteString("mode", LuceneTextNormalizer.ModeToString(s.Mode));
                writer.WriteString("suggestionText", s.SuggestionText);
                writer.WriteNumber("confidence", ToConfidence(s.Score));
                writer.WriteString("source", s.Source);
                writer.WriteString("matchedHash", s.MatchedHash);
                writer.WriteString("matchedOriginalPreview", s.MatchedOriginalPreview);
                writer.WriteString("createdUtc", s.CreatedUtc.ToString("O"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            // Safe fallback (do not crash pipeline)
            return aiNotesJson ?? "{}";
        }
    }

    private static double ToConfidence(double luceneScore)
    {
        // deterministic, safe normalization (not ML)
        // Typical Lucene scores vary widely, clamp
        if (luceneScore <= 0) return 0.0;
        if (luceneScore >= 10) return 0.99;

        var norm = luceneScore / 10.0;
        if (norm < 0.05) norm = 0.05;
        if (norm > 0.99) norm = 0.99;
        return Math.Round(norm, 4);
    }
}