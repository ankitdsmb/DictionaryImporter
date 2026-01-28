using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Common
{
    public class PartOfSpeechConfig
    {
        public Dictionary<string, string> Abbreviations { get; set; } = new();
        public HashSet<string> FullWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> NormalizationMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}