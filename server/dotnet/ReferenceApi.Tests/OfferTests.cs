using System.Text.Json;
using Alba;
using Edge.Errands;
using ReferenceApi.Holds;

namespace ReferenceApi.Tests;

/// <summary>
/// A decline's offers (unsettled/errands.md, rule 4): place-hold at the limit offers a
/// swap for each hold, under one id, and taking one is a PUT to that id.
/// </summary>
public class OfferTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    private async Task<(string Patron, string Cookie, ErrandView Declined)> DeclinedAtTheLimit()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        foreach (var title in new[] { 1001, 1005, 1006 })
        {
            var placing = Guid.NewGuid();
            await _host.PutErrand(cookie, placing, ErrandsApi.PlaceHold(title), 202);
            await _host.DecideErrand(placing);
        }
        var fourth = Guid.NewGuid();
        await _host.PutErrand(cookie, fourth, ErrandsApi.PlaceHold(1010), 202);
        await _host.DecideErrand(fourth);
        return (patron, cookie, await _host.GetErrand(cookie, fourth));
    }

    private Task<Hold[]> Holds(string patron) => _host.GetAsJson<Hold[]>($"/holds?patronId={patron}")!;

    // What the person would choose: the offer exactly as it came.
    private static JsonElement GivingUp(OfferView offer, Hold hold) =>
        offer.Choose.Single(c => c.GetProperty("giveUp").GetGuid() == hold.Id);

    [Fact]
    public async Task at_the_limit_a_swap_for_each_hold_is_offered_under_one_id()
    {
        var (patron, _, declined) = await DeclinedAtTheLimit();

        declined.Outcome.ShouldBe(ErrandOutcome.Declined);
        var offer = declined.Offers.ShouldHaveSingleItem();
        offer.Choose.Select(c => c.GetProperty("type").GetString()).ShouldAllBe(t => t == "swap-hold");
        offer.Choose.Select(c => c.GetProperty("titleId").GetInt32()).ShouldAllBe(t => t == 1010);
        offer.Choose.Select(c => c.GetProperty("giveUp").GetGuid()).Order()
            .ShouldBe((await Holds(patron)).Select(h => h.Id).Order());
    }

    [Fact]
    public async Task taking_one_is_a_put_to_the_offers_id_and_remembers_where_it_came_from()
    {
        var (patron, cookie, declined) = await DeclinedAtTheLimit();
        var offer = declined.Offers.Single();
        var giveUp = (await Holds(patron)).First(h => h.TitleId == 1005);

        await _host.PutErrand(cookie, offer.Id, GivingUp(offer, giveUp), 202);
        await _host.DecideErrand(offer.Id);

        var taken = await _host.GetErrand(cookie, offer.Id);
        taken.Outcome.ShouldBe(ErrandOutcome.Accepted);
        taken.OfferedBy.ShouldBe(declined.Id);
        (await Holds(patron)).Select(h => h.TitleId).Order().ShouldBe([1001, 1006, 1010]);
    }

    [Fact] // a second tab, or a change of mind: the id is spent
    public async Task only_one_can_be_taken()
    {
        var (patron, cookie, declined) = await DeclinedAtTheLimit();
        var offer = declined.Offers.Single();
        var holds = await Holds(patron);
        await _host.PutErrand(cookie, offer.Id, GivingUp(offer, holds[0]), 202);

        var second = await _host.PutErrand(cookie, offer.Id, GivingUp(offer, holds[1]), 409);

        (await second.ReadAsJsonAsync<JsonElement>()).GetProperty("detail").GetString()
            .ShouldBe("One of these offers was already chosen.");
    }

    [Fact] // the same one again is a retry, and safe
    public async Task taking_the_same_one_again_is_the_same_errand()
    {
        var (patron, cookie, declined) = await DeclinedAtTheLimit();
        var offer = declined.Offers.Single();
        var choice = GivingUp(offer, (await Holds(patron))[0]);
        await _host.PutErrand(cookie, offer.Id, choice, 202);

        await _host.PutErrand(cookie, offer.Id, choice, 202);
    }

    [Fact] // closed: the id takes what was offered, not anything of the same type
    public async Task something_not_offered_cant_be_sent_to_the_id()
    {
        var (patron, cookie, declined) = await DeclinedAtTheLimit();
        var offer = declined.Offers.Single();
        var giveUp = (await Holds(patron))[0].Id;

        await _host.PutErrand(cookie, offer.Id, ErrandsApi.SwapHold(giveUp, 1007), 409);
        await _host.PutErrand(cookie, offer.Id, ErrandsApi.PlaceHold(1010), 409);
        await _host.Scenario(s =>
        {
            s.Get.Url($"/errands/{offer.Id}");
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task someone_else_cant_take_them()
    {
        var (patron, _, declined) = await DeclinedAtTheLimit();
        var offer = declined.Offers.Single();
        var other = await _host.SignIn(HoldsApi.NewPatron());

        await _host.PutErrand(other, offer.Id, GivingUp(offer, (await Holds(patron))[0]), 403);
    }

    [Fact] // most declines offer nothing, and say so the same way
    public async Task a_decline_without_offers_has_an_empty_list()
    {
        var cookie = await _host.SignIn(HoldsApi.NewPatron());
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.CancelHold(Guid.NewGuid()), 202);
        await _host.DecideErrand(id);

        var errand = await _host.GetErrand(cookie, id);
        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Offers.ShouldBeEmpty();
    }
}
