using Edge.Errands;
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
/// Withdrawn: the person took it back before it was decided (DELETE /errands/{id}). Not a
/// decision against them, so the client owes no apology.
/// </remarks>
public enum IntentOutcome { Accepted, Declined, Withdrawn }

public static class ProtocolEvents
{
    /// <summary>The group of a patron's open streams: every tab they have open.</summary>
    public static string PatronGroup(string patronId) => $"patron:{patronId}";

    /// <summary>An intent's outcome, for the user who asked.</summary>
    /// <param name="patronId">The patron to tell.</param>
    /// <param name="intentId">The command's Idempotency-Key.</param>
    /// <param name="outcome">Final: an outcome never changes.</param>
    /// <param name="reason">Why it was declined. Shown to the user, so written for a person.</param>
    /// <param name="made">Accepted, and it made something with its own id: memory will contain it.</param>
    /// <param name="offers">Declined, and what they can choose instead. Sent as an empty list when none.</param>
    public static ServerSentEvent IntentDecided(
        string patronId, Guid intentId, IntentOutcome outcome, string? reason = null, Guid? made = null,
        IReadOnlyList<OfferView>? offers = null) =>
        new(PatronGroup(patronId), "intent-decided", new
        {
            intentId,
            outcome = outcome switch
            {
                IntentOutcome.Accepted => "accepted",
                IntentOutcome.Declined => "declined",
                _ => "withdrawn",
            },
            reason,
            made,
            offers = offers ?? [],
        });

    /// <summary>Reads under these keys changed.</summary>
    /// <param name="group">Who to tell: a patron's group, or <see cref="SseGroups.Everyone"/>.</param>
    /// <param name="keys">Paths (and query) relative to the API root. See PROTOCOL §3 for how they match.</param>
    public static ServerSentEvent Invalidate(string group, params string[] keys) =>
        new(group, "invalidate", new { keys });
}
