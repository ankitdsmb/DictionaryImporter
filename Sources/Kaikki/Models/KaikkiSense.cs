using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSense
    {
        [JsonProperty("glosses")]
        public List<string> Glosses { get; set; } = [];

        [JsonProperty("examples")]
        public List<KaikkiExample>? Examples { get; set; }

        [JsonProperty("categories")]
        public JToken? Categories { get; set; }

        [JsonProperty("topics")]
        public JToken? Topics { get; set; }

        [JsonProperty("tags")]
        public JToken? Tags { get; set; }

        [JsonProperty("senseid")]
        [Newtonsoft.Json.JsonConverter(typeof(AnyToStringConverter))] // ✅ FIX
        public string? SenseId { get; set; }

        [JsonProperty("raw_glosses")]
        public List<string>? RawGlosses { get; set; }
    }

    // ✅ FIX: fully qualify base type
    public sealed class AnyToStringConverter : Newtonsoft.Json.JsonConverter<string?>
    {
        public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var token = JToken.Load(reader);

            if (token.Type == JTokenType.String)
                return token.Value<string>();

            return token.ToString(Formatting.None);
        }

        public override void WriteJson(JsonWriter writer, string? value, Newtonsoft.Json.JsonSerializer serializer)
            => writer.WriteValue(value);
    }
}