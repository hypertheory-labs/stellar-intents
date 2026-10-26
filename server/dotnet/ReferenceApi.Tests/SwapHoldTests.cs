using Alba;
using Edge.Errands;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using ReferenceApi.Holds;

namespace ReferenceApi.Tests;

/// <summary>The "swap-hold" errand: one hold given up and another placed, or neither.</summary>
public class SwapHoldTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    private async Task<(string Patron, string Cookie)> AtTheLimit()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        foreach (var title in new[] { 1001, 1005, 1006 })
        {
            var placing = Guid.NewGuid();
            await _host.PutErrand(cookie, placing, ErrandsApi.PlaceHold(title), 202);
            await _host.DecideErrand(placing);
        }
        return (patron, cookie);
    }

    private Task<Hold[]> Holds(string patron) => _host.GetAsJson<Hold[]>($"/holds?patronId={patron}")!;

    private async Task<ErrandView> Swap(string cookie, Guid giveUp, int titleId)
    {
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.SwapHold(giveUp, titleId), 202);
        await _host.DecideErrand(id);
        return await _host.GetErrand(cookie, id);
    }

    [Fact]
    public async Task at_the_limit_a_swap_gives_one_up_and_places_the_other()
    {
        var (patron, cookie) = await AtTheLimit();
        var giveUp = (await Holds(patron)).First(h => h.TitleId == 1005).Id;

        var errand = await Swap(cookie, giveUp, 1010);

        errand.Outcome.ShouldBe(ErrandOutcome.Accepted);
        var holds = await Holds(patron);
        holds.Select(h => h.TitleId).Order().ShouldBe([1001, 1006, 1010]);
        errand.Made.ShouldBe(holds.Single(h => h.TitleId == 1010).Id);
    }

    [Fact] // declined, not 403: it says nothing about whether the hold exists
    public async Task someone_elses_hold_is_declined_and_nothing_changes()
    {
        var (owner, _) = await AtTheLimit();
        var theirs = (await Holds(owner)).First().Id;
        var other = HoldsApi.NewPatron();

        var errand = await Swap(await _host.SignIn(other), theirs, 1010);

        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Reason.ShouldBe("That isn't one of your holds.");
        (await Holds(owner)).Length.ShouldBe(3);
        (await Holds(other)).ShouldBeEmpty();
    }

    [Fact] // they'd be swapping nothing, which isn't what they asked for
    public async Task a_hold_already_cancelled_is_declined_and_nothing_is_placed()
    {
        var (patron, cookie) = await AtTheLimit();
        var giveUp = (await Holds(patron)).First().Id;
        var cancelling = Guid.NewGuid();
        await _host.PutErrand(cookie, cancelling, ErrandsApi.CancelHold(giveUp), 202);
        await _host.DecideErrand(cancelling);

        var errand = await Swap(cookie, giveUp, 1010);

        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Reason.ShouldBe("That hold was already cancelled, so there's nothing to swap.");
        (await Holds(patron)).ShouldNotContain(h => h.TitleId == 1010);
    }

    [Fact] // the limit is checked again, without the hold being given up
    public async Task still_over_the_limit_without_it_is_declined_and_nothing_changes()
    {
        var (patron, cookie) = await AtTheLimit();
        var giveUp = (await Holds(patron)).First().Id;
        // A fourth, the way a librarian might place one at the desk.
        await using (var session = _host.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            var fourth = Guid.NewGuid();
            session.Events.StartStream<Hold>(fourth, new HoldPlaced(fourth, patron, 1007, "CEN", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        var errand = await Swap(cookie, giveUp, 1010);

        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Reason.ShouldBe($"You already have {PlaceHoldDecider.HoldLimit} other holds, the limit.");
        (await Holds(patron)).Select(h => h.Id).ShouldContain(giveUp);
    }
}
