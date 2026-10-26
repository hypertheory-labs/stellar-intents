using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Alba;
using Edge.Errands;
using ReferenceApi.Holds;

namespace ReferenceApi.Tests;

/// <summary>Edge.Errands in the real app, rule by rule (unsettled/errands.md), through place-hold.</summary>
public class ErrandTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    private async Task<(string Patron, string Cookie)> SignedIn()
    {
        var patron = HoldsApi.NewPatron();
        return (patron, await _host.SignIn(patron));
    }

    private async Task<Hold[]> HoldsOf(string patron) =>
        (await _host.GetAsJson<Hold[]>($"/holds?patronId={patron}"))!;

    [Fact]
    public async Task a_new_errand_is_pending_and_says_until_when_it_can_be_taken_back()
    {
        var (_, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        var result = await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);

        result.Context.Response.Headers.Location.ToString().ShouldBe($"/errands/{id}");
        var errand = (await result.ReadAsJsonAsync<ErrandView>())!;
        errand.Outcome.ShouldBeNull();
        errand.Type.ShouldBe("place-hold");
        errand.WithdrawableUntil.ShouldBeGreaterThan(errand.RequestedAt);
    }

    [Fact] // rule 1
    public async Task accepted_places_the_hold()
    {
        var (patron, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);

        var errand = await _host.GetErrand(cookie, id);
        errand.Outcome.ShouldBe(ErrandOutcome.Accepted);
        var holds = await HoldsOf(patron);
        holds.Length.ShouldBe(1);
        holds[0].Status.ShouldBe(HoldStatus.Placed);
        // what it made: the client's guess is replaced when memory contains this id
        errand.Made.ShouldBe(holds[0].Id);
    }

    [Fact] // rule 1: a redelivered decision changes nothing
    public async Task deciding_twice_places_one_hold()
    {
        var (patron, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);
        await _host.DecideErrand(id);

        (await HoldsOf(patron)).Length.ShouldBe(1);
    }

    [Fact] // with no window it's decided at once, so deciding it here too races that
    public async Task deciding_while_it_is_being_decided_does_not_fail()
    {
        var (_, cookie) = await SignedIn();
        for (var tries = 0; tries < 20; tries++)
        {
            var id = Guid.NewGuid();
            await _host.PutErrand(cookie, id, ErrandsApi.CancelHold(Guid.NewGuid()), 202);
            // The loser fails on the version, and the retry finds the errand ended.
            await _host.DecideErrand(id);
            (await _host.GetErrand(cookie, id)).Outcome.ShouldBe(ErrandOutcome.Declined);
            // Requested and one outcome: the loser's outcome was never stored.
            await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
            (await session.Events.FetchStreamAsync(id)).Count.ShouldBe(2);
        }
    }

    [Fact] // rule 2
    public async Task over_the_limit_it_is_declined_with_a_reason()
    {
        var (patron, cookie) = await SignedIn();
        foreach (var title in new[] { 1001, 1005, 1006 })
        {
            var placed = Guid.NewGuid();
            await _host.PutErrand(cookie, placed, ErrandsApi.PlaceHold(title), 202);
            await _host.DecideErrand(placed);
        }

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1010), 202);
        await _host.DecideErrand(id);

        var errand = await _host.GetErrand(cookie, id);
        errand.Outcome.ShouldBe(ErrandOutcome.Declined);
        errand.Reason.ShouldBe("You already have 3 holds, the limit.");
        (await HoldsOf(patron)).Length.ShouldBe(3);
    }

    [Fact] // rule 5
    public async Task withdrawn_before_the_decision_it_never_happens()
    {
        var (patron, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.Withdraw(cookie, id, 204);
        await _host.DecideErrand(id); // the window closes after the withdraw

        (await _host.GetErrand(cookie, id)).Outcome.ShouldBe(ErrandOutcome.Withdrawn);
        (await HoldsOf(patron)).ShouldBeEmpty();
    }

    [Fact] // rule 5: only before the decision
    public async Task after_the_decision_it_cant_be_withdrawn()
    {
        var (_, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);

        await _host.Withdraw(cookie, id, 409);
        (await _host.GetErrand(cookie, id)).Outcome.ShouldBe(ErrandOutcome.Accepted);
    }

    [Fact]
    public async Task withdrawing_twice_is_still_204()
    {
        var (_, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.Withdraw(cookie, id, 204);
        await _host.Withdraw(cookie, id, 204);
    }

    [Fact] // PROTOCOL §1: same id, same body -> the same errand
    public async Task a_repeat_is_202_while_pending_and_200_once_decided()
    {
        var (_, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 200);
    }

    [Fact] // PROTOCOL §1: same id, different body -> 409
    public async Task the_same_id_with_a_different_ask_is_a_conflict()
    {
        var (_, cookie) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1006), 409);
    }

    [Fact] // conformance 7, across patrons: someone else's id is "a different body"
    public async Task the_same_id_from_another_patron_is_a_conflict()
    {
        var (_, cookie) = await SignedIn();
        var (_, other) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.PutErrand(other, id, ErrandsApi.PlaceHold(1001), 409);
    }

    [Fact] // §2: not recorded yet is a 404, not an outcome
    public async Task a_status_check_for_an_unknown_id_is_404()
    {
        var (_, cookie) = await SignedIn();
        await _host.Scenario(s =>
        {
            s.Get.Url($"/errands/{Guid.NewGuid()}");
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact] // closed: an errand nobody registered isn't one we do
    public async Task an_unknown_type_is_400()
    {
        var (_, cookie) = await SignedIn();
        await _host.PutErrand(cookie, Guid.NewGuid(), new { type = "launch-rocket" }, 400);
    }

    [Fact]
    public async Task a_body_without_a_type_is_400()
    {
        var (_, cookie) = await SignedIn();
        await _host.PutErrand(cookie, Guid.NewGuid(), new { titleId = 1001 }, 400);
    }

    [Fact]
    public async Task without_a_session_it_is_401()
    {
        var id = Guid.NewGuid();
        await _host.Scenario(s =>
        {
            s.Put.Json(ErrandsApi.PlaceHold(1001)).ToUrl($"/errands/{id}");
            s.WithRequestHeader("Idempotency-Key", id.ToString());
            s.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task someone_elses_errand_is_403()
    {
        var (_, cookie) = await SignedIn();
        var (_, other) = await SignedIn();
        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);

        await _host.Scenario(s =>
        {
            s.Get.Url($"/errands/{id}");
            s.WithRequestHeader("Cookie", other);
            s.StatusCodeShouldBe(403);
        });
        await _host.Withdraw(other, id, 403);
    }

    [Fact] // PROTOCOL §3: the outcome reaches the patron's streams
    public async Task the_outcome_is_an_intent_decided_on_the_stream()
    {
        var (_, cookie) = await SignedIn();
        await using var stream = await EventStream.Open(_host, cookie);
        await stream.NextOf("subscribed");

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.Withdraw(cookie, id, 204);

        var decided = await stream.NextOf("intent-decided");
        decided.GetProperty("intentId").GetGuid().ShouldBe(id);
        decided.GetProperty("outcome").GetString().ShouldBe("withdrawn");
    }

    [Fact] // PROTOCOL §3: an accepted outcome says what it made
    public async Task accepted_on_the_stream_says_what_it_made()
    {
        var (patron, cookie) = await SignedIn();
        await using var stream = await EventStream.Open(_host, cookie);
        await stream.NextOf("subscribed");

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);

        var decided = await stream.NextOf("intent-decided");
        decided.GetProperty("outcome").GetString().ShouldBe("accepted");
        decided.GetProperty("made").GetGuid().ShouldBe((await HoldsOf(patron)).Single().Id);
    }
}
