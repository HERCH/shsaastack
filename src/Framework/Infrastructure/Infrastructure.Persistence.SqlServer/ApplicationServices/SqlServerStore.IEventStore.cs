using System.Text.Json;
using Common;
using Dapper;
using Domain.Interfaces.Entities;
using Infrastructure.Persistence.Interfaces;

namespace Infrastructure.Persistence.SqlServer.ApplicationServices;

partial class SqlServerStore : IEventStore
{
    private const string EventStreamTableName = "EventStreams";

    public async Task<Result<string, Error>> AddEventsAsync<TAggregateRoot>(
        string aggregateRootId,
        List<EventSourcedChangeEvent> events,
        CancellationToken cancellationToken) where TAggregateRoot : class, IEventingAggregateRoot
    {
        try
        {
            var aggregateType = typeof(TAggregateRoot).Name;

            await ExecuteWithConnectionAsync(async connection =>
            {
                foreach (var @event in events)
                {
                    var eventData = JsonSerializer.Serialize(@event, JsonOptions);

                    var sql = $@"
                        INSERT INTO [{EventStreamTableName}]
                        (AggregateRootId, AggregateType, EventType, EventData, Version, CreatedAtUtc)
                        VALUES (@AggregateRootId, @AggregateType, @EventType, @EventData, @Version, @CreatedAtUtc)";

                    await connection.ExecuteAsync(sql, new
                    {
                        AggregateRootId = aggregateRootId,
                        AggregateType = aggregateType,
                        EventType = @event.RootEventName,
                        EventData = eventData,
                        Version = @event.Version,
                        CreatedAtUtc = @event.OccurredUtc
                    });
                }

                return true;
            }, cancellationToken);

            return aggregateRootId;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to add events for aggregate {aggregateRootId}: {ex.Message}");
        }
    }

#if TESTINGONLY
    public async Task<Result<Error>> DestroyAllAsync<TAggregateRoot>(CancellationToken cancellationToken)
        where TAggregateRoot : class, IEventingAggregateRoot
    {
        try
        {
            var aggregateType = typeof(TAggregateRoot).Name;

            var sql = $"DELETE FROM [{EventStreamTableName}] WHERE AggregateType = @AggregateType";

            await ExecuteWithConnectionAsync(async connection =>
            {
                await connection.ExecuteAsync(sql, new { AggregateType = aggregateType });
                return true;
            }, cancellationToken);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to destroy events for aggregate type {typeof(TAggregateRoot).Name}: {ex.Message}");
        }
    }
#endif

    public async Task<Result<IReadOnlyList<EventSourcedChangeEvent>, Error>> GetEventStreamAsync<TAggregateRoot>(
        string aggregateRootId,
        IEventSourcedChangeEventMigrator eventMigrator,
        CancellationToken cancellationToken) where TAggregateRoot : class, IEventingAggregateRoot
    {
        try
        {
            var aggregateType = typeof(TAggregateRoot).Name;

            var sql = $@"
                SELECT EventData, Version
                FROM [{EventStreamTableName}]
                WHERE AggregateRootId = @AggregateRootId AND AggregateType = @AggregateType
                ORDER BY Version ASC";

            var events = await ExecuteWithConnectionAsync(async connection =>
            {
                var rows = await connection.QueryAsync<EventRow>(sql, new
                {
                    AggregateRootId = aggregateRootId,
                    AggregateType = aggregateType
                });

                return rows.Select(row =>
                {
                    var @event = JsonSerializer.Deserialize<EventSourcedChangeEvent>(row.EventData, JsonOptions)!;
                    // Apply any migrations to the event
                    return eventMigrator.Migrate(@event);
                }).ToList();
            }, cancellationToken);

            return (IReadOnlyList<EventSourcedChangeEvent>)events;
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"Failed to get event stream for aggregate {aggregateRootId}: {ex.Message}");
        }
    }

    private class EventRow
    {
        public string EventData { get; set; } = string.Empty;
        public int Version { get; set; }
    }
}
