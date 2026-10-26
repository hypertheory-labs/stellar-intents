using Edge.Errands;
using Fisher;
using JasperFx.Events.Documents;

namespace ReferenceApi.Holds;

/// <summary>The "place-hold" errand: the library's rule, and nothing about the protocol.</summary>
/// <remarks>
/// WHY the hold still starts with HoldRequested: the old PUT /holds path still uses that
/// status, until the errand path replaces it. Then Requested and Rejected leave the hold,
/// because they're the errand's (unsettled/edge-weight.md).
///
/// WHY no invalidations here: writing the hold is enough. Streaming/Invalidations.cs
/// hears the commit and sends the hold's keys.
/// </remarks>
public class PlaceHoldDecider : IDecide<PlaceHold>
{
    public async Task<Decision> Decide(PlaceHold ask, Errand errand, IDocumentSession session, CancellationToken cancellation)
    {
        var placed = await session.Query<Hold>()
            .Where(h => h.PatronId == errand.Requester && h.Status == HoldStatus.Placed)
            .CountAsync(cancellation);
        if (placed >= EvaluateHoldHandler.HoldLimit)
            return Decision.Decline($"You already have {EvaluateHoldHandler.HoldLimit} holds, the limit.");

        // The hold's id is its own; the errand that placed it has another.
        var holdId = Guid.CreateVersion7();
        session.Events.StartStream<Hold>(holdId,
            new HoldRequested(holdId, errand.Requester, ask.TitleId, ask.BranchCode, errand.RequestedAt),
            new HoldPlaced(holdId, DateTimeOffset.UtcNow));
        return Decision.Accept();
    }
}
