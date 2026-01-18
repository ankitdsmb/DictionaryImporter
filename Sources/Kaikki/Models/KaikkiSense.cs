using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSense
    {
        public List<string> Glosses { get; set; } = [];

        public List<KaikkiExample>? Examples { get; set; }

        public List<string> Categories { get; set; }

        public List<string> Topics { get; set; }

        public List<string> Tags { get; set; }

        public string? SenseId { get; set; }

        public List<string>? RawGlosses { get; set; }
        public List<KaikkiSynonym> Synonyms { get; set; } = [];
        public Dictionary<string, object>? RawData { get; set; }
    }
}