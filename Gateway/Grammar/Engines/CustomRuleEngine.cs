using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DictionaryImporter.Gateway.Grammar.Core.Models;

namespace DictionaryImporter.Gateway.Grammar.Engines
{
    public  class CustomRuleEngine
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
            if (string.IsNullOrWhiteSpace(_rulesFilePath))
            {
                lock (_lock)
                {
                    _rules.Clear();
                }
                return;
            }

            if (!File.Exists(_rulesFilePath))
            {
                lock (_lock)
                {
                    _rules.Clear();
                }
                return;
            }

            try
            {
                var json = File.ReadAllText(_rulesFilePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    lock (_lock)
                    {
                        _rules.Clear();
                    }
                    return;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var loadedRules = JsonSerializer.Deserialize<List<GrammarPatternRule>>(json, options);

                var normalized = NormalizeAndValidateRules(loadedRules);

                lock (_lock)
                {
                    _rules.Clear();
                    _rules.AddRange(normalized);
                }
            }
            catch
            {
                lock (_lock)
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
                rulesCopy = _rules
                    .Select(CloneRule)
                    .ToList();
            }

            var directory = Path.GetDirectoryName(_rulesFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                rulesCopy,
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(_rulesFilePath, json);
        }

        public IReadOnlyList<GrammarPatternRule> GetRules()
        {
            lock (_lock)
            {
                return _rules
                    .Select(CloneRule)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void AddRule(GrammarPatternRule rule)
        {
            if (rule == null)
                return;

            var normalized = NormalizeAndValidateRules([rule]).FirstOrDefault();
            if (normalized == null)
                return;

            lock (_lock)
            {
                _rules.Add(normalized);
                SortRulesInPlace(_rules);
            }
        }

        public void RemoveRule(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return;

            lock (_lock)
            {
                _rules.RemoveAll(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public async Task TrainAsync(GrammarFeedback feedback)
        {
            if (feedback?.OriginalIssue == null)
                return;

            if (string.IsNullOrWhiteSpace(feedback.OriginalIssue.RuleId))
                return;

            if (!feedback.OriginalIssue.RuleId.StartsWith("PATTERN_", StringComparison.Ordinal))
                return;

            var ruleId = feedback.OriginalIssue.RuleId.Replace("PATTERN_", "", StringComparison.Ordinal);

            lock (_lock)
            {
                var rule = _rules.FirstOrDefault(r =>
                    string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));

                if (rule == null)
                    return;

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

                SortRulesInPlace(_rules);
            }

            await SaveRulesAsync();
        }

        // NEW METHOD (added)
        public int ReloadRules()
        {
            LoadRules();
            lock (_lock)
            {
                return _rules.Count;
            }
        }

        // NEW METHOD (added)
        public int GetRuleCount()
        {
            lock (_lock)
            {
                return _rules.Count;
            }
        }

        private static List<GrammarPatternRule> NormalizeAndValidateRules(List<GrammarPatternRule>? loadedRules)
        {
            if (loadedRules == null || loadedRules.Count == 0)
                return [];

            var result = new List<GrammarPatternRule>(loadedRules.Count);

            foreach (var r in loadedRules)
            {
                if (r == null)
                    continue;

                var normalized = NormalizeRule(r);
                if (normalized == null)
                    continue;

                if (!IsRuleValid(normalized))
                    continue;

                result.Add(normalized);
            }

            SortRulesInPlace(result);
            return result;
        }

        private static GrammarPatternRule? NormalizeRule(GrammarPatternRule rule)
        {
            var id = rule.Id?.Trim();
            var pattern = rule.Pattern?.Trim();
            var replacement = rule.Replacement ?? string.Empty;

            if (string.IsNullOrWhiteSpace(id))
                return null;

            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            return new GrammarPatternRule
            {
                Id = id,
                Category = string.IsNullOrWhiteSpace(rule.Category) ? "General" : rule.Category.Trim(),
                Description = rule.Description?.Trim(),
                Pattern = pattern,
                Replacement = replacement,
                Priority = rule.Priority <= 0 ? 100 : rule.Priority,
                Enabled = rule.Enabled,
                Confidence = Clamp(rule.Confidence, 0, 100),
                UsageCount = Math.Max(0, rule.UsageCount),
                SuccessCount = Math.Max(0, rule.SuccessCount)
            };
        }

        private static bool IsRuleValid(GrammarPatternRule rule)
        {
            // Enabled=false rules are allowed to exist (for admin UI / future training)
            // but should still be regex-valid so they don't crash if enabled later.

            if (string.IsNullOrWhiteSpace(rule.Id))
                return false;

            if (string.IsNullOrWhiteSpace(rule.Pattern))
                return false;

            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.Compiled);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SortRulesInPlace(List<GrammarPatternRule> rules)
        {
            // ✅ AI-like quality depends heavily on rule order
            // Order strategy:
            // 1) Enabled first
            // 2) Higher Priority first (lower number = higher priority)
            // 3) Higher confidence first
            // 4) More successful rules first
            rules.Sort((a, b) =>
            {
                var enabledCompare = b.Enabled.CompareTo(a.Enabled);
                if (enabledCompare != 0) return enabledCompare;

                var prioCompare = a.Priority.CompareTo(b.Priority);
                if (prioCompare != 0) return prioCompare;

                var confCompare = b.Confidence.CompareTo(a.Confidence);
                if (confCompare != 0) return confCompare;

                var successCompare = b.SuccessCount.CompareTo(a.SuccessCount);
                if (successCompare != 0) return successCompare;

                return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static GrammarPatternRule CloneRule(GrammarPatternRule r)
        {
            return new GrammarPatternRule
            {
                Id = r.Id,
                Category = r.Category,
                Description = r.Description,
                Pattern = r.Pattern,
                Replacement = r.Replacement,
                Priority = r.Priority,
                Enabled = r.Enabled,
                Confidence = r.Confidence,
                UsageCount = r.UsageCount,
                SuccessCount = r.SuccessCount
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
