using System.Data;

namespace DictionaryImporter.AI.Infrastructure;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();

    Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

public class SqlConnectionFactory(
    IOptions<DatabaseOptions> options,
    ILogger<SqlConnectionFactory> logger)
    : IDbConnectionFactory, IDisposable
{
    private readonly string _connectionString = options.Value.ConnectionString;

    public IDbConnection CreateConnection()
    {
        try
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create database connection");
            throw;
        }
    }

    public async Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create database connection asynchronously");
            throw;
        }
    }

    public void Dispose()
    {
    }
}