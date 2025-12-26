namespace bixo_api.Models.Enums;

/// <summary>
/// Shortlist outcome states - determines final disposition and payment.
/// Immutable once set to a terminal state (Delivered, NoMatch, Cancelled).
/// </summary>
public enum ShortlistOutcome
{
    /// <summary>No outcome decision made yet - shortlist in progress</summary>
    Pending = 0,

    /// <summary>Shortlist successfully delivered with quality candidates</summary>
    Delivered = 1,

    /// <summary>Partial delivery - some candidates but not full count (requires manual pricing)</summary>
    Partial = 2,

    /// <summary>No suitable candidates found - no delivery, no charge</summary>
    NoMatch = 3,

    /// <summary>Request cancelled by company or admin</summary>
    Cancelled = 4
}

/// <summary>
/// Helper for validating outcome transitions
/// </summary>
public static class ShortlistOutcomeTransitions
{
    private static readonly Dictionary<ShortlistOutcome, HashSet<ShortlistOutcome>> ValidTransitions = new()
    {
        // Pending can transition to any terminal state
        [ShortlistOutcome.Pending] = new() 
        { 
            ShortlistOutcome.Delivered, 
            ShortlistOutcome.Partial, 
            ShortlistOutcome.NoMatch, 
            ShortlistOutcome.Cancelled 
        },
        
        // Terminal states cannot transition (immutable)
        [ShortlistOutcome.Delivered] = new(),
        [ShortlistOutcome.Partial] = new(),
        [ShortlistOutcome.NoMatch] = new(),
        [ShortlistOutcome.Cancelled] = new()
    };

    /// <summary>
    /// Check if an outcome transition is allowed
    /// </summary>
    public static bool IsValidTransition(ShortlistOutcome from, ShortlistOutcome to)
    {
        return ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    /// <summary>
    /// Check if an outcome is terminal (immutable)
    /// </summary>
    public static bool IsTerminal(ShortlistOutcome outcome)
    {
        return outcome != ShortlistOutcome.Pending;
    }

    /// <summary>
    /// Validate and throw if transition is not allowed
    /// </summary>
    public static void ValidateTransition(ShortlistOutcome from, ShortlistOutcome to)
    {
        if (!IsValidTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Invalid outcome transition: {from} ? {to}. " +
                (IsTerminal(from) 
                    ? $"Outcome {from} is immutable and cannot be changed." 
                    : $"Allowed transitions from {from}: {string.Join(", ", ValidTransitions[from])}"));
        }
    }
}
