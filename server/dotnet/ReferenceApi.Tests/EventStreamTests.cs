using System.Text.Json;
using Alba;

namespace ReferenceApi.Tests;

/// <summary>PROTOCOL §3: the event stream.</summary>
public class EventStreamTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    [Fact] // identity: 401, never a redirect to a login page
    public async Task without_a_session_the_stream_is_401()
    {
        await _host.Scenario(s =>
        {
            s.Get.Url("/events");
            s.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task subscribed_comes_first()
    {
        await using var stream = await EventStream.Open(_host, await _host.SignIn(HoldsApi.NewPatron()));
        (await stream.Next()).EventType.ShouldBe("subscribed");
    }

    [Fact]
    public async Task heartbeats_keep_an_idle_stream_alive()
    {
        await using var stream = await EventStream.Open(_host, await _host.SignIn(HoldsApi.NewPatron()));
        await stream.NextOf("subscribed");
        (await stream.Next(TimeSpan.FromSeconds(3))).EventType.ShouldBe("heartbeat");
    }

    [Fact]
    public async Task a_declined_errand_is_an_intent_decided_with_the_reason()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        foreach (var title in new[] { 1001, 1005, 1006 })
        {
            var placed = Guid.NewGuid();
            await _host.PutErrand(cookie, placed, ErrandsApi.PlaceHold(title), 202);
            await _host.DecideErrand(placed);
        }
        await using var stream = await EventStream.Open(_host, cookie);
        await stream.NextOf("subscribed");

        var fourth = Guid.NewGuid();
        await _host.PutErrand(cookie, fourth, ErrandsApi.PlaceHold(1010), 202);
        await _host.DecideErrand(fourth);

        var decided = await stream.NextOf("intent-decided");
        decided.GetProperty("intentId").GetGuid().ShouldBe(fourth);
        decided.GetProperty("outcome").GetString().ShouldBe("declined");
        decided.GetProperty("detail").GetString().ShouldBe("You already have 3 holds, the limit.");
        // the same offers GET /errands/{id} has: one id, a swap for each hold
        var offers = decided.GetProperty("offers");
        offers.GetArrayLength().ShouldBe(1);
        offers[0].GetProperty("choose").GetArrayLength().ShouldBe(3);
    }

    [Fact] // stream rules: fan out per connection
    public async Task two_tabs_both_hear_the_decision()
    {
        var cookie = await _host.SignIn(HoldsApi.NewPatron());
        await using var tab1 = await EventStream.Open(_host, cookie);
        await using var tab2 = await EventStream.Open(_host, cookie);
        await tab1.NextOf("subscribed");
        await tab2.NextOf("subscribed");

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);

        (await tab1.NextOf("intent-decided")).GetProperty("intentId").GetGuid().ShouldBe(id);
        (await tab2.NextOf("intent-decided")).GetProperty("intentId").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task the_patron_hears_which_of_their_reads_changed()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        await using var stream = await EventStream.Open(_host, cookie);
        await stream.NextOf("subscribed");

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);

        var keys = (await stream.AllOf("invalidate", TimeSpan.FromSeconds(1.5))).SelectMany(Keys).ToList();
        keys.ShouldContain($"/holds?patronId={patron}");
        keys.ShouldContain("/holds?titleId=1001");
    }

    [Fact] // PROTOCOL §3: invalidations caused by other users; decisions only to the one who asked
    public async Task someone_else_hears_the_wait_list_changed_but_not_the_decision()
    {
        var asker = HoldsApi.NewPatron();
        var askerCookie = await _host.SignIn(asker);
        await using var someoneElse = await EventStream.Open(_host, await _host.SignIn(HoldsApi.NewPatron()));
        await someoneElse.NextOf("subscribed");

        var id = Guid.NewGuid();
        await _host.PutErrand(askerCookie, id, ErrandsApi.PlaceHold(1006), 202);
        await _host.DecideErrand(id);

        var heard = new List<string>();
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
        while (DateTime.UtcNow < until)
        {
            var item = await someoneElse.Next();
            if (item.EventType == "invalidate") heard.AddRange(Keys(JsonDocument.Parse(item.Data).RootElement));
            item.EventType.ShouldNotBe("intent-decided");
        }

        heard.ShouldContain("/holds?titleId=1006");
        heard.ShouldNotContain($"/holds?patronId={asker}");
    }

    private static IEnumerable<string> Keys(JsonElement invalidate) =>
        invalidate.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!);
}
