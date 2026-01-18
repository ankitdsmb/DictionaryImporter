using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiRelated
    {
        public string? Word { get; set; }
        public string? Type { get; set; } // "see also", "compare", "derived", etc.
        public string? Sense { get; set; }
    }
}