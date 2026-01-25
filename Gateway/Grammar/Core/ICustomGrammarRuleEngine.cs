using DictionaryImporter.Gateway.Grammar.Engines;

namespace DictionaryImporter.Gateway.Grammar.Core;

public interface ICustomGrammarRuleEngine
{
    CustomRuleEngine Engine { get; }
}

public sealed class CustomGrammarRuleEngine(CustomRuleEngine engine) : ICustomGrammarRuleEngine
{
    public CustomRuleEngine Engine { get; } = engine;
}