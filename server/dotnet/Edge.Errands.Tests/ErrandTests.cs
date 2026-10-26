using Edge.Errands;

namespace Edge.Errands.Tests;

/// <summary>unsettled/errands.md, rules 1 and 2: an errand ends once, and a decline says why.</summary>
public class ErrandTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static Errand Requested() => Errand.Create(
        new ErrandRequested(Guid.NewGuid(), "place-hold", "P1", "{}", At, At.AddSeconds(3)));

    [Fact]
    public void a_new_errand_is_pending_and_withdrawable_until_its_decision()
    {
        var errand = Requested();
        errand.Pending.ShouldBeTrue();
        errand.Outcome.ShouldBeNull();
        errand.WithdrawableUntil.ShouldBe(At.AddSeconds(3));
    }

    [Fact]
    public void a_declined_errand_keeps_its_reason()
    {
        var errand = Requested();
        errand.Apply(new ErrandDeclined(errand.Id, "You already have 3 holds.", At));
        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Reason.ShouldBe("You already have 3 holds.");
    }

    [Fact]
    public void the_first_outcome_stands()
    {
        var errand = Requested();
        errand.Apply(new ErrandWithdrawn(errand.Id, At));
        errand.Apply(new ErrandAccepted(errand.Id, At.AddSeconds(1)));
        errand.Outcome.ShouldBe(ErrandOutcome.Withdrawn);
        errand.EndedAt.ShouldBe(At);
    }

    [Fact]
    public void a_later_decline_doesnt_add_a_reason()
    {
        var errand = Requested();
        errand.Apply(new ErrandAccepted(errand.Id, At));
        errand.Apply(new ErrandDeclined(errand.Id, "Too late.", At));
        errand.Outcome.ShouldBe(ErrandOutcome.Accepted);
        errand.Reason.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void a_decline_without_a_reason_cant_be_made(string reason) =>
        Should.Throw<ArgumentException>(() => Decision.Decline(reason));

    [Fact]
    public void a_decline_with_a_reason_can()
    {
        var decision = Decision.Decline("You already have 3 holds.");
        decision.ShouldBeOfType<Decision.Declined>().Reason.ShouldBe("You already have 3 holds.");
    }
}
