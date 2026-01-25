namespace DictionaryImporter.Gateway.Rewriter
{
    public sealed class RewriteRuleHitBuffer
    {
        private readonly Dictionary<string, long> _hits = new(StringComparer.Ordinal);

        public void Add(string sourceCode, string mode, string ruleType, string ruleKey, long count = 1)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();
            mode = string.IsNullOrWhiteSpace(mode) ? "UNKNOWN" : mode.Trim();
            ruleType = string.IsNullOrWhiteSpace(ruleType) ? "UNKNOWN" : ruleType.Trim();
            ruleKey = string.IsNullOrWhiteSpace(ruleKey) ? string.Empty : ruleKey.Trim();

            if (string.IsNullOrWhiteSpace(ruleKey))
                return;

            if (count <= 0)
                count = 1;

            var key = $"{sourceCode}|{mode}|{ruleType}|{ruleKey}";

            if (_hits.TryGetValue(key, out var existing))
                _hits[key] = existing + count;
            else
                _hits[key] = count;
        }

        public IReadOnlyList<RewriteRuleHitUpsert> Flush()
        {
            if (_hits.Count == 0)
                return Array.Empty<RewriteRuleHitUpsert>();

            var list = new List<RewriteRuleHitUpsert>(_hits.Count);

            foreach (var kv in _hits)
            {
                var parts = kv.Key.Split('|', 4);
                if (parts.Length != 4)
                    continue;

                list.Add(new RewriteRuleHitUpsert
                {
                    SourceCode = parts[0],
                    Mode = parts[1],
                    RuleType = parts[2],
                    RuleKey = parts[3],
                    HitCount = kv.Value
                });
            }

            _hits.Clear();
            return list;
        }
    }
}