namespace DictionaryImporter.Infrastructure.Parsing.ExampleExtractor
{
    public sealed class KaikkiExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "KAIKKI";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            try
            {
                // Try to extract from raw fragment first (JSON)
                if (!string.IsNullOrWhiteSpace(parsed.RawFragment) &&
                    parsed.RawFragment.StartsWith("{") && parsed.RawFragment.EndsWith("}"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parsed.RawFragment, options);

                    if (rawData != null && rawData.TryGetValue("examples", out var examplesElement))
                    {
                        foreach (var example in examplesElement.EnumerateArray())
                        {
                            if (example.TryGetProperty("text", out var textProp) &&
                                textProp.ValueKind == JsonValueKind.String)
                            {
                                var text = textProp.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    examples.Add(CleanExampleText(text));
                                }
                            }
                        }
                    }
                }

                // Fallback: extract from formatted definition
                if (examples.Count == 0 && !string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    examples.AddRange(ExtractExamplesFromDefinition(parsed.Definition));
                }
            }
            catch (Exception)
            {
                // Silent fail, try definition extraction
                if (!string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    examples.AddRange(ExtractExamplesFromDefinition(parsed.Definition));
                }
            }

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();
        }

        private List<string> ExtractExamplesFromDefinition(string definition)
        {
            var examples = new List<string>();

            if (!definition.Contains("【Examples】"))
                return examples;

            var lines = definition.Split('\n');
            var inExamplesSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Examples】"))
                {
                    inExamplesSection = true;
                    continue;
                }

                if (inExamplesSection)
                {
                    if (trimmedLine.StartsWith("【")) // New section started
                        break;

                    if (trimmedLine.StartsWith("• "))
                    {
                        var example = trimmedLine.Substring(2).Trim();

                        // Remove translation part if present
                        var pipeIndex = example.IndexOf('|');
                        if (pipeIndex > 0)
                            example = example.Substring(0, pipeIndex).Trim();

                        if (!string.IsNullOrWhiteSpace(example))
                            examples.Add(CleanExampleText(example));
                    }
                }
            }

            return examples;
        }

        private string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            // Remove quotation marks if present
            example = example.Trim('"', '\'', '`', '«', '»', '「', '」', '『', '』');

            // Ensure proper ending punctuation
            if (!example.EndsWith(".") && !example.EndsWith("!") && !example.EndsWith("?"))
            {
                example += ".";
            }

            return example.Trim();
        }
    }
}