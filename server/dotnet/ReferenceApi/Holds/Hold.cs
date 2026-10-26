using JasperFx;

namespace ReferenceApi.Holds;

// What happened to a hold. A hold exists once it's placed; asking for one, and being
// told no, belong to the place-hold errand (Edge.Errands), not to the hold.
public record HoldPlaced(Guid HoldId, string PatronId, int TitleId, string BranchCode, DateTimeOffset PlacedAt);
public record HoldCancelled(Guid HoldId, DateTimeOffset CancelledAt);

public enum HoldStatus { Placed, Cancelled }

// Built from its events, and snapshotted inline so it can be queried.
/// <summary>A hold as it stands now.</summary>
public class Hold : IRevisioned
{
    public Guid Id { get; set; }
    // IRevisioned: a projected document's revision is its stream version.
    /// <summary>The hold's version. Sent as the ETag.</summary>
    public int Version { get; set; }
    public string PatronId { get; set; } = "";
    public int TitleId { get; set; }
    public string BranchCode { get; set; } = "";
    public HoldStatus Status { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public static Hold Create(HoldPlaced e) => new()
    {
        Id = e.HoldId,
        PatronId = e.PatronId,
        TitleId = e.TitleId,
        BranchCode = e.BranchCode,
        Status = HoldStatus.Placed,
        PlacedAt = e.PlacedAt,
    };

    public void Apply(HoldCancelled e)
    {
        Status = HoldStatus.Cancelled;
        CancelledAt = e.CancelledAt;
    }
}
