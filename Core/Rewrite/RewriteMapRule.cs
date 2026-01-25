namespace DictionaryImporter.Core.Rewrite
{
    public sealed class RewriteMapRule
    {
        public long RewriteMapId { get; set; }

        public string FromText { get; set; } = string.Empty;

        public string ToText { get; set; } = string.Empty;

        public bool WholeWord { get; set; }

        public bool IsRegex { get; set; }

        public int Priority { get; set; } = 100;

        public bool Enabled { get; set; } = true;

        public string? SourceCode { get; set; }

        public RewriteTargetMode Mode { get; set; } = RewriteTargetMode.Definition;
    }
}