namespace ReferenceApi.Streaming;

/*
  The messages the event stream carries (PROTOCOL §3). They know nothing about holds:
  any part of the app that decides an intent or changes a read publishes these, and
  EventStreamHub fans them out.

  WHY publish these from the domain code (and not have the stream watch domain events):
  it's the lowest rung. A service only has to say "intent X was decided" and "these reads
  changed", never expose its own events. Deriving them from events is a later rung.

  Always publish them as Wolverine messages (cascading, or through IMessageBus inside a
  transactional handler), never by calling the hub directly: then they're sent only after
  the transaction commits, and the browser can never hear about something that didn't
  happen.
*/

public enum IntentOutcome { Done, NeedsAttention }

/// <summary>An intent's outcome, for the user who asked.</summary>
/// <param name="Audience">The patron to tell.</param>
/// <param name="IntentId">The command's Idempotency-Key.</param>
/// <param name="Outcome">Final: an outcome never changes.</param>
/// <param name="Detail">Shown to the user, so written for a person (same rules as a problem's detail).</param>
public record IntentDecided(string Audience, Guid IntentId, IntentOutcome Outcome, string? Detail = null);

/// <summary>Reads under these keys changed.</summary>
/// <param name="Audience">The patron to tell, or null for everyone connected.</param>
/// <param name="Keys">Paths (and query) relative to the API root. See PROTOCOL §3 for how they match.</param>
public record Invalidate(string? Audience, string[] Keys);

/// <summary>Hands committed protocol messages to the hub.</summary>
public static class ProtocolMessageHandler
{
    public static void Handle(IntentDecided decided, EventStreamHub hub) =>
        hub.Publish(decided.Audience, "intent-decided", new
        {
            intentId = decided.IntentId,
            outcome = decided.Outcome == IntentOutcome.Done ? "done" : "needs-attention",
            detail = decided.Detail,
        });

    public static void Handle(Invalidate invalidate, EventStreamHub hub) =>
        hub.Publish(invalidate.Audience, "invalidate", new { keys = invalidate.Keys });
}
