using System.Data;
using Common;
using Microsoft.Data.SqlClient;

namespace Infrastructure.Persistence.SqlServer.ApplicationServices;

/// <summary>
///     Provides a SQL Server-based implementation of data, event, and blob stores
/// </summary>
public sealed partial class SqlServerStore : IDisposable
{
    private readonly SqlServerSettings _settings;
    private bool _disposed;

    private SqlServerStore(SqlServerSettings settings)
    {
        settings.ThrowIfInvalid(nameof(settings));
        _settings = settings;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    public static SqlServerStore Create(SqlServerSettings settings)
    {
        return new SqlServerStore(settings);
    }

    private async Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<T> ExecuteWithConnectionAsync<T>(
        Func<IDbConnection, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken);
        return await action(connection);
    }
}
