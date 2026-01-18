using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSense
    {
        public List<string> Glosses { get; set; } = [];
        public List<KaikkiExample> Examples { get; set; } = [];
        public List<KaikkiSynonym> Synonyms { get; set; } = [];
        public List<KaikkiAntonym> Antonyms { get; set; } = [];
        public List<KaikkiRelated> Related { get; set; } = [];
        public List<string> Categories { get; set; } = [];
        public List<string> Topics { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        public List<KaikkiForm> Forms { get; set; } = [];
        public Dictionary<string, object>? RawData { get; set; }

        public int SenseNumber { get; set; }
        public string PartOfSpeech { get; set; } = null!;
        public string Definition { get; set; } = null!;
        public string? GrammarInfo { get; set; }
        public string? DomainLabel { get; set; }
        public string? UsageNote { get; set; }
    }
}