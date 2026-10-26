using Edge.Errands;
using Fisher;
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

    public async Task<Decision> Decide(PlaceHold ask, Errand errand, IDocumentSession session, CancellationToken cancellation)
    {
        var placed = await session.Query<Hold>()
            .Where(h => h.PatronId == errand.Requester && h.Status == HoldStatus.Placed)
            .CountAsync(cancellation);
        if (placed >= HoldLimit)
            return Decision.Decline($"You already have {HoldLimit} holds, the limit.");

        // The hold's id is its own; the errand that placed it has another, so it says what it made.
        var holdId = Guid.CreateVersion7();
        session.Events.StartStream<Hold>(holdId,
            new HoldPlaced(holdId, errand.Requester, ask.TitleId, ask.BranchCode, DateTimeOffset.UtcNow));
        return Decision.Accept(made: holdId);
    }
}
