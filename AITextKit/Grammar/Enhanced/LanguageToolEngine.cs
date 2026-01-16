namespace DictionaryImporter.AITextKit.Grammar.Enhanced;

public sealed partial class LanguageToolEngine(string languageToolUrl, ILogger<LanguageToolEngine> logger)
    : IGrammarEngine
{
    private readonly string _languageToolUrl =
        languageToolUrl ?? throw new ArgumentNullException(nameof(languageToolUrl));

    private readonly ILogger<LanguageToolEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string Name => "LanguageTool";
    public double ConfidenceWeight => 0.85;

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        try
        {
            var request = new
            {
                text = text,
                language = languageCode,
                enabledOnly = false
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_languageToolUrl}/v2/check",
                content,
                ct);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<LanguageToolResponse>(responseJson);

                if (result?.Matches != null)
                {
                    issues.AddRange(from match in result.Matches
                                    let replacements = match.Replacements?.Select(r => r.Value)
                                        .ToList() ?? []
                                    select new GrammarIssue(StartOffset: match.Offset, EndOffset: match.Offset + match.Length,
                                        Message: match.Message, ShortMessage: GetShortMessageFromRule(match.Rule),
                                        Replacements: replacements, RuleId: match.Rule.Id, RuleDescription: match.Rule.Description,
                                        Tags: GetTagsFromLanguageToolRule(match.Rule), Context: GetContext(text, match.Offset),
                                        ContextOffset: Math.Max(0, match.Offset - 20),
                                        ConfidenceLevel: CalculateConfidenceFromMatch(match)));
                }
            }
            else
            {
                _logger.LogWarning("LanguageTool API returned {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LanguageTool API");
        }

        sw.Stop();
        return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    }

    private static string GetContext(string text, int offset, int contextLength = 50)
    {
        var start = Math.Max(0, offset - contextLength);
        var end = Math.Min(text.Length, offset + contextLength);
        return text.Substring(start, end - start);
    }

    private static string GetShortMessageFromRule(LanguageToolRule rule)
    {
        if (!string.IsNullOrEmpty(rule.Category?.Name))
            return rule.Category.Name;

        if (!string.IsNullOrEmpty(rule.IssueType?.TypeName))
            return rule.IssueType.TypeName;

        return rule.Id.Split('_').FirstOrDefault() ?? "grammar";
    }

    private static IReadOnlyList<string> GetTagsFromLanguageToolRule(LanguageToolRule rule)
    {
        var tags = new List<string>();

        if (!string.IsNullOrEmpty(rule.Category?.Name))
            tags.Add(rule.Category.Name.ToLowerInvariant());

        if (!string.IsNullOrEmpty(rule.IssueType?.TypeName))
            tags.Add(rule.IssueType.TypeName.ToLowerInvariant());

        if (tags.Count == 0)
            tags.Add("language-tool");

        return tags;
    }

    private int CalculateConfidenceFromMatch(LanguageToolMatch match)
    {
        if (match.Rule?.IssueType?.QualityScore != null)
            return (int)(match.Rule.IssueType.QualityScore * 100);

        return match.Rule?.IssueType?.TypeName?.ToLowerInvariant() switch
        {
            "typographical" => 95,
            "grammar" => 90,
            "style" => 80,
            "spelling" => 99,
            _ => 85
        };
    }

    public bool IsSupported(string languageCode)
    {
        return languageCode switch
        {
            "en-US" or "en-GB" or "en-CA" or "en-AU" => true,
            "de-DE" or "de-AT" or "de-CH" => true,
            "fr-FR" or "fr-CA" => true,
            "es-ES" or "es-MX" => true,
            "pt-PT" or "pt-BR" => true,
            "it-IT" => true,
            "nl-NL" => true,
            "pl-PL" => true,
            "ru-RU" => true,
            _ => false
        };
    }
}

public sealed partial class LanguageToolEngine
{
    private class LanguageToolResponse
    {
        public List<LanguageToolMatch>? Matches { get; init; }
    }

    private class LanguageToolMatch
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Message { get; set; } = null!;
        public string? ShortMessage { get; set; }
        public List<LanguageToolReplacement>? Replacements { get; set; }
        public LanguageToolRule Rule { get; set; } = null!;
    }

    private class LanguageToolReplacement
    {
        public string Value { get; set; } = null!;
    }

    private class LanguageToolRule
    {
        public string Id { get; set; } = null!;
        public string Description { get; set; } = null!;
        public LanguageToolCategory? Category { get; set; }
        public LanguageToolIssueType? IssueType { get; set; }
    }

    private class LanguageToolCategory
    {
        public string Name { get; set; } = null!;
    }

    private class LanguageToolIssueType
    {
        public string TypeName { get; set; } = null!;
        public double? QualityScore { get; set; }
    }
}