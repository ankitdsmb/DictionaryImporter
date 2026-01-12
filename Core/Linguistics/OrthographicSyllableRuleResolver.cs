namespace DictionaryImporter.Core.Linguistics;

public sealed class OrthographicSyllableRuleResolver
{
    private readonly IReadOnlyList<IOrthographicSyllableRule> _rules;

    public OrthographicSyllableRuleResolver(
        IEnumerable<IOrthographicSyllableRule> rules)
    {
        _rules = rules.ToList();
    }

    public IReadOnlyList<string> ApplyRules(
        string locale,
        IReadOnlyList<string> syllables,
        string word)
    {
        var rule =
            _rules.FirstOrDefault(r => r.LocaleCode == locale);

        return rule == null
            ? syllables
            : rule.Apply(syllables, word);
    }
}