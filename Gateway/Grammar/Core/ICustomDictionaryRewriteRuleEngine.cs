using DictionaryImporter.Gateway.Grammar.Engines;

namespace DictionaryImporter.Gateway.Grammar.Core;

public interface ICustomDictionaryRewriteRuleEngine
{
    CustomRuleEngine Engine { get; }
}