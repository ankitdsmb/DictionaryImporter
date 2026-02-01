using SmartFormat;

namespace DictionaryImporter.Core.Text.Experimental;

public class SmartDefinitionFormatter
{
    public string FormatDefinition(string pattern, params object[] args)
    {
        // Example patterns for dictionary formatting
        var formats = new Dictionary<string, string>
        {
            ["sense"] = "{SenseNumber}. {Definition} ({PartOfSpeech})",
            ["example"] = "e.g., {Example}",
            ["etymology"] = "[From {Origin}]"
        };

        return Smart.Format(pattern, args);
    }

    //public string FormatSenses(List<Sense> senses)
    //{
    //    var template = @"{{SenseNumber:list:|. | and }}. {{Definition:list:|; |; and }}";
    //    return Smart.Format(template, new { senses });
    //}
}