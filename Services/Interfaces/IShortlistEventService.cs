using bixo_api.Models.Enums;

namespace bixo_api.Services.Interfaces;

/// <summary>
/// Service for emitting and recording auditable shortlist events
/// </summary>
public interface IShortlistEventService
{
    /// <summary>
    /// Emit a shortlist event for audit trail
    /// </summary>
    Task EmitAsync(ShortlistEvent evt);

    /// <summary>
    /// Get event history for a shortlist
    /// </summary>
    Task<List<ShortlistEventRecord>> GetEventsAsync(Guid shortlistRequestId);
}

/// <summary>
/// Event to be emitted
/// </summary>
public class ShortlistEvent
{
    public Guid ShortlistRequestId { get; set; }
    public ShortlistEventType EventType { get; set; }
    public ShortlistStatus? PreviousStatus { get; set; }
    public ShortlistStatus? NewStatus { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorType { get; set; } // "system", "admin", "company"
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Recorded event from database
/// </summary>
public class ShortlistEventRecord
{
    public Guid Id { get; set; }
    public Guid ShortlistRequestId { get; set; }
    public ShortlistEventType EventType { get; set; }
    public ShortlistStatus? PreviousStatus { get; set; }
    public ShortlistStatus? NewStatus { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorType { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}
