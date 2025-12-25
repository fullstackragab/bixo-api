using System.Text.Json;
using Dapper;
using bixo_api.Data;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class ShortlistEventService : IShortlistEventService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ShortlistEventService> _logger;

    public ShortlistEventService(IDbConnectionFactory db, ILogger<ShortlistEventService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EmitAsync(ShortlistEvent evt)
    {
        try
        {
            using var connection = _db.CreateConnection();

            var metadataJson = evt.Metadata != null
                ? JsonSerializer.Serialize(evt.Metadata)
                : null;

            await connection.ExecuteAsync(@"
                INSERT INTO shortlist_events (id, shortlist_request_id, event_type, previous_status, new_status,
                                              actor_id, actor_type, metadata, created_at)
                VALUES (@Id, @ShortlistRequestId, @EventType, @PreviousStatus, @NewStatus,
                        @ActorId, @ActorType, @Metadata::jsonb, @CreatedAt)",
                new
                {
                    Id = Guid.NewGuid(),
                    evt.ShortlistRequestId,
                    EventType = evt.EventType.ToString().ToLower(),
                    PreviousStatus = evt.PreviousStatus?.ToString().ToLower(),
                    NewStatus = evt.NewStatus?.ToString().ToLower(),
                    evt.ActorId,
                    evt.ActorType,
                    Metadata = metadataJson,
                    CreatedAt = DateTime.UtcNow
                });

            _logger.LogInformation(
                "Shortlist event emitted: {EventType} for {ShortlistId}, {PreviousStatus} → {NewStatus}",
                evt.EventType, evt.ShortlistRequestId, evt.PreviousStatus, evt.NewStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit shortlist event: {EventType} for {ShortlistId}",
                evt.EventType, evt.ShortlistRequestId);
            // Don't throw - event emission should not block business logic
        }
    }

    public async Task<List<ShortlistEventRecord>> GetEventsAsync(Guid shortlistRequestId)
    {
        using var connection = _db.CreateConnection();

        var events = await connection.QueryAsync<dynamic>(@"
            SELECT id, shortlist_request_id, event_type, previous_status, new_status,
                   actor_id, actor_type, metadata::text, created_at
            FROM shortlist_events
            WHERE shortlist_request_id = @ShortlistRequestId
            ORDER BY created_at ASC",
            new { ShortlistRequestId = shortlistRequestId });

        return events.Select(e => new ShortlistEventRecord
        {
            Id = (Guid)e.id,
            ShortlistRequestId = (Guid)e.shortlist_request_id,
            EventType = Enum.TryParse<ShortlistEventType>((string)e.event_type, true, out var et) ? et : ShortlistEventType.StateChanged,
            PreviousStatus = ParseStatus(e.previous_status as string),
            NewStatus = ParseStatus(e.new_status as string),
            ActorId = e.actor_id as Guid?,
            ActorType = e.actor_type as string,
            Metadata = e.metadata as string,
            CreatedAt = (DateTime)e.created_at
        }).ToList();
    }

    private static ShortlistStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrEmpty(status)) return null;
        return Enum.TryParse<ShortlistStatus>(status, true, out var s) ? s : null;
    }
}
