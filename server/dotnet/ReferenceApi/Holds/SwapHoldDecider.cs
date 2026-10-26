using Edge.Errands;
using Fisher;
using JasperFx.Events.Documents;

namespace ReferenceApi.Holds;

/// <summary>What a patron asks for to give up one hold and place another, together.</summary>
public record SwapHold(Guid GiveUp, int TitleId, string BranchCode);

/// <summary>The "swap-hold" errand: one hold cancelled and another placed, or neither.</summary>
/// <remarks>
/// WHY one errand, and not cancel-hold then place-hold: if the cancel committed and the
/// place didn't (a hold placed from another tab in between), the patron would have given
/// up a hold for nothing, and only the browser would know (unsettled/edge-weight.md).
/// Here both happen in one transaction.
/// </remarks>
public class SwapHoldDecider : IDecide<SwapHold>
{
    public async Task<Decision> Decide(SwapHold ask, Errand errand, IDocumentSession session, CancellationToken cancellation)
    {
        // WHY for writing: if the hold is cancelled between this read and the save, the save
        // fails on the hold's version, the handler retries, and the retry declines.
        var giveUp = await session.Events.FetchForWriting<Hold>(ask.GiveUp, cancellation);
        // One reason for both: whether someone else's hold exists is none of their business.
        if (giveUp.Aggregate is null || giveUp.Aggregate.PatronId != errand.Requester)
            return Decision.Decline("That isn't one of your holds.");
        // Unlike cancelling, already-cancelled isn't what they asked for: they'd be swapping nothing.
        if (giveUp.Aggregate.Status != HoldStatus.Placed)
            return Decision.Decline("That hold was already cancelled, so there's nothing to swap.");

        var others = await session.Query<Hold>()
            .Where(h => h.PatronId == errand.Requester && h.Status == HoldStatus.Placed && h.Id != ask.GiveUp)
            .CountAsync(cancellation);
        // Another hold was placed after the one to give up was chosen.
        if (others >= PlaceHoldDecider.HoldLimit)
            return Decision.Decline($"You already have {PlaceHoldDecider.HoldLimit} other holds, the limit.");

        var now = DateTimeOffset.UtcNow;
        giveUp.AppendOne(new HoldCancelled(ask.GiveUp, now));
        var holdId = Guid.CreateVersion7();
        session.Events.StartStream<Hold>(holdId,
            new HoldPlaced(holdId, errand.Requester, ask.TitleId, ask.BranchCode, now));
        return Decision.Accept(made: holdId);
    }
}
