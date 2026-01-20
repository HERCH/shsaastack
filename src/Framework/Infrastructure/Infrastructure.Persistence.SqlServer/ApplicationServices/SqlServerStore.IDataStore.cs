using System.Data;
using System.Text.Json;
using Application.Persistence.Interfaces;
using Common;
using Dapper;
using Domain.Interfaces;
using Infrastructure.Persistence.Interfaces;
using QueryAny;

namespace Infrastructure.Persistence.SqlServer.ApplicationServices;

partial class SqlServerStore : IDataStore
{
    private const int DefaultMaxQueryResults = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public int MaxQueryResults => DefaultMaxQueryResults;

    public async Task<Result<CommandEntity, Error>> AddAsync(string containerName, CommandEntity entity,
        CancellationToken cancellationToken)
    {
        try
        {
            entity.LastPersistedAtUtc = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(entity.Properties, JsonOptions);
            var isDeleted = entity.IsDeleted.HasValue && entity.IsDeleted.Value;

            var sql = $@"
                INSERT INTO [{containerName}] (Id, Data, IsDeleted, LastPersistedAtUtc)
                VALUES (@Id, @Data, @IsDeleted, @LastPersistedAtUtc)";

            await ExecuteWithConnectionAsync(async connection =>
            {
                await connection.ExecuteAsync(sql, new
                {
                    Id = entity.Id.Value,
                    Data = json,
                    IsDeleted = isDeleted,
                    LastPersistedAtUtc = entity.LastPersistedAtUtc.Value
                });
                return true;
            }, cancellationToken);

            return entity;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to add entity to {containerName}: {ex.Message}");
        }
    }

    public async Task<Result<long, Error>> CountAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            var sql = $"SELECT COUNT(*) FROM [{containerName}] WHERE IsDeleted = 0";

            var count = await ExecuteWithConnectionAsync(async connection =>
            {
                return await connection.ExecuteScalarAsync<long>(sql);
            }, cancellationToken);

            return count;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to count entities in {containerName}: {ex.Message}");
        }
    }

#if TESTINGONLY
    public async Task<Result<Error>> DestroyAllAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            var sql = $"DELETE FROM [{containerName}]";

            await ExecuteWithConnectionAsync(async connection =>
            {
                await connection.ExecuteAsync(sql);
                return true;
            }, cancellationToken);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to destroy all entities in {containerName}: {ex.Message}");
        }
    }
#endif

    public async Task<Result<QueryResults<QueryEntity>, Error>> QueryAsync<TQueryableEntity>(
        string containerName,
        QueryClause<TQueryableEntity> query,
        PersistedEntityMetadata metadata,
        CancellationToken cancellationToken) where TQueryableEntity : IQueryableEntity
    {
        try
        {
            // Build SQL query from QueryClause
            var (sql, parameters) = BuildSqlQuery(containerName, query);

            var entities = await ExecuteWithConnectionAsync(async connection =>
            {
                var rows = await connection.QueryAsync<SqlRow>(sql, parameters);
                return rows.Select(row => DeserializeEntity(row.Data, metadata)).ToList();
            }, cancellationToken);

            var results = new QueryResults<QueryEntity>(entities);
            return results;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to query entities in {containerName}: {ex.Message}");
        }
    }

    public async Task<Result<Error>> RemoveAsync(string containerName, string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = $@"
                UPDATE [{containerName}]
                SET IsDeleted = 1, LastPersistedAtUtc = @LastPersistedAtUtc
                WHERE Id = @Id";

            await ExecuteWithConnectionAsync(async connection =>
            {
                await connection.ExecuteAsync(sql, new
                {
                    Id = id,
                    LastPersistedAtUtc = DateTime.UtcNow
                });
                return true;
            }, cancellationToken);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to remove entity from {containerName}: {ex.Message}");
        }
    }

    public async Task<Result<Optional<CommandEntity>, Error>> ReplaceAsync(string containerName, string id,
        CommandEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            entity.LastPersistedAtUtc = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(entity.Properties, JsonOptions);
            var isDeleted = entity.IsDeleted.HasValue && entity.IsDeleted.Value;

            var sql = $@"
                UPDATE [{containerName}]
                SET Data = @Data, IsDeleted = @IsDeleted, LastPersistedAtUtc = @LastPersistedAtUtc
                WHERE Id = @Id AND IsDeleted = 0";

            var rowsAffected = await ExecuteWithConnectionAsync(async connection =>
            {
                return await connection.ExecuteAsync(sql, new
                {
                    Id = id,
                    Data = json,
                    IsDeleted = isDeleted,
                    LastPersistedAtUtc = entity.LastPersistedAtUtc.Value
                });
            }, cancellationToken);

            if (rowsAffected == 0)
            {
                return Optional<CommandEntity>.None;
            }

            return entity.ToOptional();
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to replace entity in {containerName}: {ex.Message}");
        }
    }

    public async Task<Result<Optional<CommandEntity>, Error>> RetrieveAsync(string containerName, string id,
        PersistedEntityMetadata metadata, CancellationToken cancellationToken)
    {
        try
        {
            var sql = $"SELECT Data FROM [{containerName}] WHERE Id = @Id AND IsDeleted = 0";

            var json = await ExecuteWithConnectionAsync(async connection =>
            {
                return await connection.QuerySingleOrDefaultAsync<string>(sql, new { Id = id });
            }, cancellationToken);

            if (json == null)
            {
                return Optional<CommandEntity>.None;
            }

            var entity = DeserializeCommandEntity(json, metadata);
            return entity.ToOptional();
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to retrieve entity from {containerName}: {ex.Message}");
        }
    }

    private static (string Sql, object Parameters) BuildSqlQuery<TQueryableEntity>(
        string containerName,
        QueryClause<TQueryableEntity> query) where TQueryableEntity : IQueryableEntity
    {
        // Simple implementation - just get all non-deleted records
        // In production, this should parse the query and build proper WHERE clauses
        var sql = $@"
            SELECT TOP {DefaultMaxQueryResults} Data
            FROM [{containerName}]
            WHERE IsDeleted = 0
            ORDER BY LastPersistedAtUtc DESC";

        return (sql, new { });
    }

    private static QueryEntity DeserializeEntity(string json, PersistedEntityMetadata metadata)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions);
        var hydrationProperties = new HydrationProperties(properties ?? new Dictionary<string, object?>());
        var entity = QueryEntity.FromHydrationProperties(hydrationProperties, metadata);
        return entity;
    }

    private static CommandEntity DeserializeCommandEntity(string json, PersistedEntityMetadata metadata)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions);
        var hydrationProperties = new HydrationProperties(properties ?? new Dictionary<string, object?>());
        var entity = CommandEntity.FromHydrationProperties(hydrationProperties, metadata);
        return entity;
    }

    private class SqlRow
    {
        public string Data { get; set; } = string.Empty;
    }
}
