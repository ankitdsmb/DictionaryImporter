namespace DictionaryImporter.Core.Validation
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; }
        public string? Reason { get; }

        private ValidationResult(bool isValid, string? reason)
        {
            IsValid = isValid;
            Reason = reason;
        }

        public static ValidationResult Valid()
            => new(true, null);

        public static ValidationResult Invalid(string reason)
            => new(false, reason);
    }
}