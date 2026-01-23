namespace DictionaryImporter.Domain.Models
{
    public static class GraphRelations
    {
        public const string HasSense = "HAS_SENSE";
        public const string SubSenseOf = "SUB_SENSE_OF";
        public const string InDomain = "IN_DOMAIN";
        public const string DerivedFrom = "DERIVED_FROM";
        public const string See = "SEE";
        public const string RelatedTo = "RELATED_TO";
        public const string Compare = "COMPARE";
        public const string BelongsTo = "BELONGS_TO";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>
            {
                HasSense,
                SubSenseOf,
                InDomain,
                DerivedFrom,
                See,
                RelatedTo,
                Compare,
                BelongsTo
            };
    }
}