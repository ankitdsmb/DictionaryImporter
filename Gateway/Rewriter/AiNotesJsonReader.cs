namespace DictionaryImporter.Gateway.Rewriter
{
    internal static class AiNotesJsonReader
    {
        public static (string Original, string Rewritten) TryReadTitle(string? aiNotesJson)
        {
            if (string.IsNullOrWhiteSpace(aiNotesJson))
                return (string.Empty, string.Empty);

            try
            {
                using var doc = JsonDocument.Parse(aiNotesJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("title", out var titleObj))
                    return (string.Empty, string.Empty);

                var original = titleObj.TryGetProperty("original", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                var rewritten = titleObj.TryGetProperty("rewritten", out var r) ? r.GetString() ?? string.Empty : string.Empty;

                return (original.Trim(), rewritten.Trim());
            }
            catch
            {
                return (string.Empty, string.Empty);
            }
        }

        public static IReadOnlyList<(string Original, string Rewritten)> TryReadExamples(string? aiNotesJson, int maxExamples)
        {
            if (string.IsNullOrWhiteSpace(aiNotesJson))
                return Array.Empty<(string, string)>();

            if (maxExamples <= 0)
                maxExamples = 10;

            if (maxExamples > 50)
                maxExamples = 50;

            try
            {
                using var doc = JsonDocument.Parse(aiNotesJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("examples", out var examplesArr))
                    return Array.Empty<(string, string)>();

                if (examplesArr.ValueKind != JsonValueKind.Array)
                    return Array.Empty<(string, string)>();

                var list = new List<(string Original, string Rewritten)>();

                foreach (var item in examplesArr.EnumerateArray())
                {
                    if (list.Count >= maxExamples)
                        break;

                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var original = item.TryGetProperty("original", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                    var rewritten = item.TryGetProperty("rewritten", out var r) ? r.GetString() ?? string.Empty : string.Empty;

                    original = original.Trim();
                    rewritten = rewritten.Trim();

                    if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(rewritten))
                        continue;

                    list.Add((original, rewritten));
                }

                return list;
            }
            catch
            {
                return Array.Empty<(string, string)>();
            }
        }
    }
}
