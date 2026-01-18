using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiHeadTemplate
    {
        public string? Name { get; set; }
        public Dictionary<string, string> Args { get; set; } = new();
        public string? Expansion { get; set; }
    }
}