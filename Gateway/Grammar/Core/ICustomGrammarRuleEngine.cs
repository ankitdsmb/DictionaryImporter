using DictionaryImporter.Gateway.Grammar.Engines;

namespace DictionaryImporter.Gateway.Grammar.Core;

public interface ICustomGrammarRuleEngine
{
    CustomRuleEngine Engine { get; }
}