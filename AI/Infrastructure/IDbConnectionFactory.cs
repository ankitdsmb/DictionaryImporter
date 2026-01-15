using System.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();

    Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}