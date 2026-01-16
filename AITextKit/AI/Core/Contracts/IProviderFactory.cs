namespace DictionaryImporter.AITextKit.AI.Core.Contracts
{
    public interface IProviderFactory
    {
        ICompletionProvider CreateProvider(string providerName);

        IEnumerable<ICompletionProvider> GetProvidersForType(RequestType requestType);

        IEnumerable<ICompletionProvider> GetAllProviders();

        Task<bool> ValidateConfigurationAsync(CancellationToken cancellationToken = default);
    }
}