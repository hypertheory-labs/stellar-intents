using JasperFx;

namespace Edge.Errands;

/// <summary>How an errand ended. It ends exactly once.</summary>
public enum ErrandOutcome { Accepted, Declined, Withdrawn }

/// <summary>
/// What a decline offered instead: one id, and the asks it can be used for. Taking one is
/// PUT /errands/{Id} with that ask, so only one can be taken (the same id with another body
/// is a 409).
/// </summary>
/// <param name="Choose">Each ask as JSON, with its "type", exactly as the person sends it back.</param>
public record ErrandOffers(Guid Id, IReadOnlyList<string> Choose);

// What happened to an errand. Requested first; then exactly one of the other three.
/// <param name="OfferedBy">The declined errand that offered this one, if it was taken from an offer.</param>
public record ErrandRequested(
    Guid ErrandId,
    string Type,
    string Requester,
    string Ask,
    DateTimeOffset RequestedAt,
    DateTimeOffset DecideAt,
    Guid? OfferedBy = null);
public record ErrandAccepted(Guid ErrandId, DateTimeOffset AcceptedAt, Guid? Made = null);
public record ErrandDeclined(Guid ErrandId, string Reason, DateTimeOffset DeclinedAt, ErrandOffers? Offers = null);
public record ErrandWithdrawn(Guid ErrandId, DateTimeOffset WithdrawnAt);

/// <summary>An errand as it stands now. Built from its events.</summary>
public class Errand : IRevisioned
{
    public Guid Id { get; set; }
    public int Version { get; set; }

    /// <summary>The errand type's name, as registered (e.g. "place-hold").</summary>
    public string Type { get; set; } = "";

    /// <summary>Who asked. The app says who that is, from the request (never from the body).</summary>
    public string Requester { get; set; } = "";

    /// <summary>What they asked for: the request body, as JSON.</summary>
    public string Ask { get; set; } = "";

    /// <summary>Null while it's pending; set exactly once.</summary>
    public ErrandOutcome? Outcome { get; set; }

    /// <summary>What accepting it made, if it made something with an id of its own.</summary>
    public Guid? Made { get; set; }

    /// <summary>Why it was declined. Only set when it was.</summary>
    public string? Reason { get; set; }

    /// <summary>What the decline offered instead, if anything.</summary>
    public ErrandOffers? Offers { get; set; }

    /// <summary>The declined errand whose offer this was, if it was one.</summary>
    public Guid? OfferedBy { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>
    /// The decision happens at this time, not before, so until then the person can take it
    /// back for free (unsettled/errands.md, rule 5).
    /// </summary>
    public DateTimeOffset WithdrawableUntil { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public bool Pending => Outcome is null;

    public static Errand Create(ErrandRequested e) => new()
    {
        Id = e.ErrandId,
        Type = e.Type,
        Requester = e.Requester,
        Ask = e.Ask,
        RequestedAt = e.RequestedAt,
        WithdrawableUntil = e.DecideAt,
        OfferedBy = e.OfferedBy,
    };

    public void Apply(ErrandAccepted e)
    {
        if (End(ErrandOutcome.Accepted, e.AcceptedAt)) Made = e.Made;
    }

    public void Apply(ErrandDeclined e)
    {
        if (!End(ErrandOutcome.Declined, e.DeclinedAt)) return;
        Reason = e.Reason;
        Offers = e.Offers;
    }

    public void Apply(ErrandWithdrawn e) => End(ErrandOutcome.Withdrawn, e.WithdrawnAt);

    // WHY the first outcome stands, and a second is ignored rather than thrown: what stops
    // a second outcome being stored is the stream's version (the handlers fetch for
    // writing). But Fisher applies inline projections before it checks the version, so a
    // throw here would beat that check and fail the race as a crash, not a retry.
    private bool End(ErrandOutcome outcome, DateTimeOffset at)
    {
        if (!Pending) return false;
        Outcome = outcome;
        EndedAt = at;
        return true;
    }
}

/// <summary>
/// A decline's offers, kept under their id, so a PUT to that id can tell it was offered,
/// to whom, and by which errand.
/// </summary>
public class ErrandOffer
{
    /// <summary>The offers' id: the id the taken errand gets.</summary>
    public Guid Id { get; set; }
    /// <summary>The declined errand that made the offers.</summary>
    public Guid From { get; set; }
    public string Requester { get; set; } = "";
    public List<string> Choose { get; set; } = [];
}
