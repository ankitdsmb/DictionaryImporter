using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Sources.Kaikki.Models
{
    public static class KaikkiTokenExtensions
    {
        public static List<string> ToStringList(this JToken? token)
        {
            if (token == null)
                return [];

            // ["a","b"]
            if (token.Type == JTokenType.Array)
            {
                var list = new List<string>();

                foreach (var child in token.Children())
                {
                    // element is string
                    if (child.Type == JTokenType.String)
                    {
                        var s = child.Value<string>();
                        if (!string.IsNullOrWhiteSpace(s))
                            list.Add(s.Trim());

                        continue;
                    }

                    // element is object => try common patterns
                    if (child.Type == JTokenType.Object)
                    {
                        // examples:
                        // { "name": "Category:Foo" }
                        // { "category": "Foo" }
                        // { "value": "Foo" }
                        var obj = (JObject)child;

                        var maybe =
                            obj["name"]?.Value<string>() ??
                            obj["category"]?.Value<string>() ??
                            obj["value"]?.Value<string>() ??
                            obj["text"]?.Value<string>();

                        if (!string.IsNullOrWhiteSpace(maybe))
                            list.Add(maybe.Trim());

                        continue;
                    }

                    // numbers/bools/etc => ignore
                }

                return list.Distinct().ToList();
            }

            // "single"
            if (token.Type == JTokenType.String)
            {
                var s = token.Value<string>();
                return string.IsNullOrWhiteSpace(s) ? [] : [s.Trim()];
            }

            // object => try extract common properties
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;

                var maybe =
                    obj["name"]?.Value<string>() ??
                    obj["category"]?.Value<string>() ??
                    obj["value"]?.Value<string>() ??
                    obj["text"]?.Value<string>();

                return string.IsNullOrWhiteSpace(maybe) ? [] : [maybe.Trim()];
            }

            return [];
        }
    }
}