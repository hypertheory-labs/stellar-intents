using Edge.Errands;
using JasperFx.Events.Documents;

namespace ReferenceApi.Holds;

/// <summary>What a patron asks for to place a hold. Whose it is comes from the session (PROTOCOL §0).</summary>
public record PlaceHold(int TitleId, string BranchCode);

/// <summary>The "place-hold" errand: the library's rule, and nothing about the protocol.</summary>
/// <remarks>
/// WHY no invalidations here: writing the hold is enough. Streaming/Invalidations.cs
/// hears the commit and sends the hold's keys.
/// </remarks>
public class PlaceHoldDecider : IDecide<PlaceHold>
{
    /// <summary>The library's rule, deliberately simple: at most this many holds per patron.</summary>
    public const int HoldLimit = 3;

    public async Task<Decision> Decide(PlaceHold ask, Errand errand, ErrandSession session, CancellationToken cancellation)
    {
        var placed = await session.Query<Hold>()
            .Where(h => h.PatronId == errand.Requester && h.Status == HoldStatus.Placed)
            .OrderBy(h => h.PlacedAt)
            .ToListAsync(cancellation);
        // Less than they asked for, so it's a decline, with what they can choose instead:
        // give up one of their holds for this one (unsettled/errands.md, rule 2).
        if (placed.Count >= HoldLimit)
            return Decision.Decline($"You already have {HoldLimit} holds, the limit.",
                offers: placed.Select(h => new SwapHold(h.Id, ask.TitleId, ask.BranchCode)));

        // The hold's id is its own; the errand that placed it has another, so it says what it made.
        var holdId = Guid.CreateVersion7();
        session.Events.StartStream<Hold>(holdId,
            new HoldPlaced(holdId, errand.Requester, ask.TitleId, ask.BranchCode, DateTimeOffset.UtcNow));
        return Decision.Accept(made: holdId);
    }
}
