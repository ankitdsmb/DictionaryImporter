namespace DictionaryImporter.Core.Validation;

public sealed class ValidationResult
{
    private ValidationResult(bool isValid, string? reason)
    {
        IsValid = isValid;
        Reason = reason;
    }

    public bool IsValid { get; }
    public string? Reason { get; }

    public static ValidationResult Valid()
    {
        return new ValidationResult(true, null);
    }

    public static ValidationResult Invalid(string reason)
    {
        return new ValidationResult(false, reason);
    }
}