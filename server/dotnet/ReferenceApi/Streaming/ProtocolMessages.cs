using Edge.Sse;

namespace ReferenceApi.Streaming;

/*
  The events the stream carries (PROTOCOL §3), as Edge.Sse messages. They know nothing
  about holds: any part of the app that decides an intent or changes a read returns these.

  WHY publish these from the domain code (and not have the stream watch domain events):
  it's the lowest rung. A service only has to say "intent X was decided" and "these reads
  changed", never expose its own events. Deriving them from events is a later rung.

  Always return or publish them from a transactional handler (never call the hub
  directly): then they're sent only after the transaction commits, and the browser can
  never hear about something that didn't happen.
*/

/// <summary>How an intent ended. Final either way.</summary>
/// <remarks>
/// Withdrawn: the person took it back before it was decided (here, a cancel that got there
/// first). Not a decision against them, so the client owes no apology.
/// </remarks>
public enum IntentOutcome { Accepted, Declined, Withdrawn }

public static class ProtocolEvents
{
    /// <summary>An intent's outcome, for the user who asked.</summary>
    /// <param name="audience">The patron to tell.</param>
    /// <param name="intentId">The command's Idempotency-Key.</param>
    /// <param name="outcome">Final: an outcome never changes.</param>
    /// <param name="detail">Shown to the user, so written for a person (same rules as a problem's detail).</param>
    public static ServerSentEvent IntentDecided(string audience, Guid intentId, IntentOutcome outcome, string? detail = null) =>
        new(audience, "intent-decided", new
        {
            intentId,
            outcome = outcome switch
            {
                IntentOutcome.Accepted => "accepted",
                IntentOutcome.Declined => "declined",
                _ => "withdrawn",
            },
            detail,
        });

    /// <summary>Reads under these keys changed.</summary>
    /// <param name="audience">The patron to tell, or null for everyone connected.</param>
    /// <param name="keys">Paths (and query) relative to the API root. See PROTOCOL §3 for how they match.</param>
    public static ServerSentEvent Invalidate(string? audience, params string[] keys) =>
        new(audience, "invalidate", new { keys });
}
