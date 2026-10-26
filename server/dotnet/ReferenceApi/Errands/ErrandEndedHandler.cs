using Edge.Errands;
using Edge.Sse;
using ReferenceApi.Streaming;

namespace ReferenceApi.Errands;

/// <summary>Tells the patron's open streams how an errand ended (PROTOCOL §3, intent-decided).</summary>
/// <remarks>
/// This is the app's half of "the library knows when, the app knows how". Today "how" is
/// only the stream: a convenience. Promised messages (by the patron's channels) aren't
/// built (unsettled/errands.md, rule 9).
/// </remarks>
public static class ErrandEndedHandler
{
    public static ServerSentEvent Handle(ErrandEnded ended) =>
        ProtocolEvents.IntentDecided(ended.Requester, ended.ErrandId, ended.Outcome switch
        {
            ErrandOutcome.Accepted => IntentOutcome.Accepted,
            ErrandOutcome.Declined => IntentOutcome.Declined,
            _ => IntentOutcome.Withdrawn,
        }, ended.Reason);
}
