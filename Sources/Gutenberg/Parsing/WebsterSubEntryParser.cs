namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    public sealed class WebsterSubEntryParser(ILogger<WebsterSubEntryParser> logger) : IDictionaryDefinitionParser
    {
        private static readonly Regex NumberedSenseRegex =
            new(
                @"(?<!\w)(?<num>\d+)\.\s+(?<body>.*?)(?=(\s+\d+\.\s+|$))",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex LetteredSubSenseRegex =
            new(
                @"\((?<letter>[a-z])\)\s+(?<body>.*?)(?=(\([a-z]\)\s+|$))",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex IdiomSplitRegex =
            new(
                @"--\s*(?<body>[^-]+)",
                RegexOptions.Compiled);

        private static readonly Regex IdiomParseRegex =
            new(
                @"^(?<title>(To\s+)?[A-Z][A-Za-z\s\-']+?)\s*,?\s*(?<def>.+)$",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex AliasRegex =
            new(
                @"\(\s*or\s+(?<alias>[^)]+)\)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CrossRefRegex =
            new(
                @"\b(?<type>See also|See|Cf\.)\s+(?<target>[A-Z][A-Za-z\s\-']+)\.?",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Definition))
                yield break;

            logger.LogDebug(
                "Parsing Webster entry | Word={Word} | EntryId={Id}",
                entry.Word,
                entry.DictionaryEntryId);

            var definition = entry.Definition.Trim();

            yield return new ParsedDefinition
            {
                MeaningTitle = entry.Word,
                SenseNumber = null,
                Definition = string.Empty,
                RawFragment = definition,
                ParentKey = "headword"
            };

            logger.LogDebug(
                "Root parsed | Word={Word}",
                entry.Word);

            var numbered = NumberedSenseRegex.Matches(definition);

            if (numbered.Count > 0)
            {
                logger.LogDebug(
                    "Numbered senses detected | Word={Word} | Count={Count}",
                    entry.Word,
                    numbered.Count);

                foreach (Match sense in numbered)
                {
                    var senseNumber =
                        int.Parse(sense.Groups["num"].Value);

                    var body =
                        sense.Groups["body"].Value.Trim();

                    var senseKey =
                        $"sense:{senseNumber}";

                    yield return BuildParsed(
                        entry.Word,
                        senseNumber,
                        body,
                        body,
                        "headword",
                        senseKey);

                    foreach (var sub in ParseLetteredSubSenses(
                                 entry.Word,
                                 body,
                                 senseNumber,
                                 senseKey))
                        yield return sub;
                }

                yield break;
            }

            if (IsValidMeaningTitle(entry.Word))
            {
                logger.LogDebug(
                    "Single unnumbered sense | Word={Word}",
                    entry.Word);

                yield return BuildParsed(
                    entry.Word,
                    null,
                    definition,
                    definition,
                    "headword",
                    null);
            }

            foreach (var idiom in ParseIdioms(entry))
                yield return idiom;
        }

        private IEnumerable<ParsedDefinition> ParseLetteredSubSenses(
            string word,
            string body,
            int senseNumber,
            string parentKey)
        {
            var subs =
                LetteredSubSenseRegex.Matches(body);

            if (subs.Count == 0)
                yield break;

            logger.LogDebug(
                "Lettered sub-senses detected | Word={Word} | Sense={Sense} | Count={Count}",
                word,
                senseNumber,
                subs.Count);

            foreach (Match sub in subs)
                yield return BuildParsed(
                    word,
                    senseNumber,
                    sub.Groups["body"].Value.Trim(),
                    sub.Value.Trim(),
                    parentKey,
                    null);
        }

        private IEnumerable<ParsedDefinition> ParseIdioms(
            DictionaryEntry entry)
        {
            var matches =
                IdiomSplitRegex.Matches(entry.Definition);

            foreach (Match m in matches)
            {
                var raw =
                    m.Groups["body"].Value.Trim();

                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var parsed =
                    IdiomParseRegex.Match(raw);

                if (!parsed.Success)
                    continue;

                var title =
                    parsed.Groups["title"].Value.Trim();

                if (!IsValidMeaningTitle(title))
                    continue;

                var def =
                    parsed.Groups["def"].Value.Trim();

                logger.LogDebug(
                    "Idiom parsed | Word={Word} | Idiom={Idiom}",
                    entry.Word,
                    title);

                yield return BuildParsed(
                    title,
                    null,
                    def,
                    "-- " + raw,
                    "headword",
                    null);
            }
        }

        private ParsedDefinition BuildParsed(
            string title,
            int? senseNumber,
            string definition,
            string raw,
            string parentKey,
            string? selfKey)
        {
            var usage =
                WebsterUsageExtractor.Extract(ref definition);

            var domain =
                WebsterDomainExtractor.Extract(ref definition);

            var aliasMatch =
                AliasRegex.Match(definition);

            var alias =
                aliasMatch.Success
                    ? aliasMatch.Groups["alias"].Value.Trim()
                    : null;

            var crossRefs =
                ExtractCrossReferences(definition);

            return new ParsedDefinition
            {
                MeaningTitle = title.Trim(),
                SenseNumber = senseNumber,
                Definition = definition.Trim(),
                RawFragment = raw,
                Alias = alias,
                Domain = domain,
                UsageLabel = usage,
                CrossReferences = crossRefs,
                ParentKey = parentKey,
                SelfKey = selfKey
            };
        }

        private static IReadOnlyList<CrossReference> ExtractCrossReferences(
            string text)
        {
            var list = new List<CrossReference>();

            foreach (Match m in CrossRefRegex.Matches(text))
                list.Add(new CrossReference
                {
                    TargetWord = m.Groups["target"].Value.Trim(),
                    ReferenceType = m.Groups["type"].Value
                        .Replace(".", string.Empty)
                        .Replace(" ", string.Empty)
                });

            return list;
        }

        private static bool IsValidMeaningTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            if (title.StartsWith("[") || title.StartsWith("("))
                return false;

            return true;
        }
    }
}