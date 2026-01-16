namespace DictionaryImporter.AITextKit.Grammar.Enhanced
{
    public sealed class CustomRuleEngine
    {
        private readonly List<GrammarPatternRule> _rules = [];
        private readonly string _rulesFilePath;
        private readonly object _lock = new();

        public CustomRuleEngine(string rulesFilePath)
        {
            _rulesFilePath = rulesFilePath;
            LoadRules();
        }

        public void LoadRules()
        {
            if (File.Exists(_rulesFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_rulesFilePath);
                    var loadedRules = JsonSerializer.Deserialize<List<GrammarPatternRule>>(json);
                    if (loadedRules != null)
                    {
                        lock (_lock)
                        {
                            _rules.Clear();
                            _rules.AddRange(loadedRules);
                        }
                    }
                }
                catch (Exception)
                {
                    _rules.Clear();
                }
            }
        }

        public async Task SaveRulesAsync()
        {
            List<GrammarPatternRule> rulesCopy;
            lock (_lock)
            {
                rulesCopy = new List<GrammarPatternRule>(_rules);
            }

            var json = JsonSerializer.Serialize(rulesCopy, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_rulesFilePath, json);
        }

        public IReadOnlyList<GrammarPatternRule> GetRules() => _rules.AsReadOnly();

        public void AddRule(GrammarPatternRule rule)
        {
            lock (_lock)
            {
                _rules.Add(rule);
            }
        }

        public void RemoveRule(string ruleId)
        {
            lock (_lock)
            {
                _rules.RemoveAll(r => r.Id == ruleId);
            }
        }

        public async Task TrainAsync(GrammarFeedback feedback)
        {
            if (feedback.OriginalIssue == null || !feedback.OriginalIssue.RuleId.StartsWith("PATTERN_"))
                return;

            var ruleId = feedback.OriginalIssue.RuleId.Replace("PATTERN_", "");

            lock (_lock)
            {
                var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
                if (rule != null)
                {
                    rule.UsageCount++;

                    if (feedback.IsFalsePositive)
                    {
                        rule.Confidence = Math.Max(0, rule.Confidence - 10);
                    }
                    else if (feedback.IsValidCorrection)
                    {
                        rule.SuccessCount++;
                        rule.Confidence = Math.Min(100, rule.Confidence + 5);
                    }
                }
            }

            await SaveRulesAsync();
        }
    }
}