using Edge.Errands;
using Fisher;

namespace ReferenceApi.Holds;

/// <summary>What a patron asks for to give up a placed hold.</summary>
public record CancelHold(Guid HoldId);

/// <summary>The "cancel-hold" errand: a forward errand, after a hold was placed.</summary>
/// <remarks>
/// WHY its own errand, and not withdrawing the place-hold errand: withdrawing happens only
/// before a decision. Once the hold is placed, it existed, and giving it up moves forward
/// and can be declined (unsettled/errands.md, rule 6). The old DELETE /holds/{id} did
/// both jobs, which is how the cancel-first bug was possible.
///
/// WHY no window: nothing is gained by waiting. The reason the place-hold errand waits
/// (to be taken back for free) doesn't apply to giving something up.
/// </remarks>
public class CancelHoldDecider : IDecide<CancelHold>
{
    public async Task<Decision> Decide(CancelHold ask, Errand errand, IDocumentSession session, CancellationToken cancellation)
    {
        var hold = await session.Events.FetchLatest<Hold>(ask.HoldId, cancellation);
        // One reason for both: whether someone else's hold exists is none of their business.
        if (hold is null || hold.PatronId != errand.Requester)
            return Decision.Decline("That isn't one of your holds.");

        // Already cancelled: what they asked for is already true (rule 2: what was asked, or better).
        if (hold.Status == HoldStatus.Placed)
            session.Events.Append(hold.Id, new HoldCancelled(hold.Id, DateTimeOffset.UtcNow));
        return Decision.Accept();
    }
}
