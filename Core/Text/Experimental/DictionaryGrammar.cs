//// Install: dotnet add package Irony
//using Irony.Parsing;

//public class DictionaryGrammar : Grammar
//{
//    public DictionaryGrammar()
//    {
//        // Define grammar for dictionary definitions
//        var definition = new NonTerminal("definition");
//        var sense = new NonTerminal("sense");
//        var example = new NonTerminal("example");

//        // Custom parsing rules for dictionary formats
//        definition.Rule = MakeStarRule(definition, sense);
//        sense.Rule = ToTerm("1.") + "[A-Z].*";

//        this.Root = definition;
//    }
//}

//public class IronyNormalizer : IDefinitionNormalizer
//{
//    public string Normalize(string raw)
//    {
//        var grammar = new DictionaryGrammar();
//        var parser = new Parser(grammar);
//        var parseTree = parser.Parse(raw);

//        // Extract normalized structure
//        return ExtractNormalizedDefinition(parseTree);
//    }
//}