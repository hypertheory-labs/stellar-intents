using Alba;
using Edge.Errands;
using ReferenceApi.Holds;

namespace ReferenceApi.Tests;

/// <summary>The "cancel-hold" errand: giving up a placed hold moves forward (unsettled/errands.md, rule 6).</summary>
public class CancelHoldTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    private async Task<(string Patron, string Cookie, Guid HoldId)> WithAPlacedHold()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        var placing = Guid.NewGuid();
        await _host.PutErrand(cookie, placing, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(placing);
        var holds = (await _host.GetAsJson<Hold[]>($"/holds?patronId={patron}"))!;
        return (patron, cookie, holds.Single().Id);
    }

    private async Task<ErrandView> Cancel(string cookie, Guid holdId)
    {
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.CancelHold(holdId), 202);
        await _host.DecideErrand(id);
        return await _host.GetErrand(cookie, id);
    }

    [Fact]
    public async Task a_placed_hold_is_cancelled()
    {
        var (patron, cookie, holdId) = await WithAPlacedHold();

        (await Cancel(cookie, holdId)).Outcome.ShouldBe(ErrandOutcome.Accepted);
        (await _host.GetAsJson<Hold[]>($"/holds?patronId={patron}"))!.ShouldBeEmpty();
    }

    [Fact] // what they asked for is already true
    public async Task a_cancelled_hold_cancelled_again_is_accepted()
    {
        var (_, cookie, holdId) = await WithAPlacedHold();
        await Cancel(cookie, holdId);

        (await Cancel(cookie, holdId)).Outcome.ShouldBe(ErrandOutcome.Accepted);
    }

    [Fact] // declined, not 403: it's a decision about the errand, and says nothing about the hold
    public async Task someone_elses_hold_is_declined_and_stays()
    {
        var (owner, _, holdId) = await WithAPlacedHold();
        var other = await _host.SignIn(HoldsApi.NewPatron());

        var errand = await Cancel(other, holdId);

        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Reason.ShouldBe("That isn't one of your holds.");
        (await _host.GetAsJson<Hold[]>($"/holds?patronId={owner}"))!.Length.ShouldBe(1);
    }

    [Fact] // the two jobs DELETE /holds/{id} used to share, apart
    public async Task after_the_hold_is_placed_withdrawing_is_refused_and_cancelling_works()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        var placing = Guid.NewGuid();
        await _host.PutErrand(cookie, placing, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(placing);
        var holdId = (await _host.GetAsJson<Hold[]>($"/holds?patronId={patron}"))!.Single().Id;

        await _host.Withdraw(cookie, placing, 409);
        (await Cancel(cookie, holdId)).Outcome.ShouldBe(ErrandOutcome.Accepted);
    }
}
