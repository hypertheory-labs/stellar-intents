using Edge.Sse;
using ReferenceApi.Streaming;

namespace ReferenceApi.Holds;

/// <summary>Which reads a change to a hold makes stale, written by hand (the baseline).</summary>
/// <remarks>
/// The reads a hold appears in: the hold itself, its patron's list, and its title's wait
/// list. Patrons see only their own holds; anyone may be looking at a title's wait list.
///
/// WHY exact keys with query strings: PROTOCOL §3 matches a key to a read by equality or
/// by a path prefix, and a prefix can't pick out a query parameter. "/holds" would cover
/// both lists, but for every patron, on every change. Equality is precise, but only if
/// the client asks with exactly this query (no extra parameters, same order).
///
/// LATER: derive these instead of writing them (from events, or DCB tags); see the cart.
/// </remarks>
public static class HoldKeys
{
    public static ServerSentEvent[] Changed(Hold hold) =>
    [
        ProtocolEvents.Invalidate(hold.PatronId, $"/holds/{hold.Id}", $"/holds?patronId={hold.PatronId}"),
        ProtocolEvents.Invalidate(null, $"/holds?titleId={hold.TitleId}"),
    ];
}
