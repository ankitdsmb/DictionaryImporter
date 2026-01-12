namespace DictionaryImporter.Infrastructure.Verification;

public interface IPostMergeVerifier
{
    Task VerifyAsync(
        string sourceCode,
        CancellationToken ct);
}