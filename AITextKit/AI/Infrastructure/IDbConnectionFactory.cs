namespace DictionaryImporter.AITextKit.AI.Infrastructure;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();

    Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}