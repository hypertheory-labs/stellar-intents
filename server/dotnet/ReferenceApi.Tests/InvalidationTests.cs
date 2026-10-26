using System.Text.Json;
using Alba;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using ReferenceApi.Holds;

namespace ReferenceApi.Tests;

/// <summary>
/// PROTOCOL §3, "invalidate": sent for what a commit wrote (Streaming/Invalidations.cs),
/// not for what a handler remembered to say.
/// </summary>
public class InvalidationTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    // Other tests share the app, and the title's wait list goes to everyone, so look
    // only for this patron's list.
    private static bool ForPatron(JsonElement invalidate, string patron) =>
        invalidate.GetProperty("keys").EnumerateArray().Any(k => k.GetString() == $"/holds?patronId={patron}");

    [Fact] // an errand's decider wrote the hold, and never said which reads changed
    public async Task an_accepted_errand_makes_the_patrons_holds_stale()
    {
        var patron = HoldsApi.NewPatron();
        var cookie = await _host.SignIn(patron);
        await using var stream = await EventStream.Open(_host, cookie);
        await stream.NextOf("subscribed");

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1001), 202);
        await _host.DecideErrand(id);

        (await stream.AllOf("invalidate", TimeSpan.FromSeconds(2))).ShouldContain(i => ForPatron(i, patron));
    }

    [Fact] // a decline writes no hold, so no hold's reads are stale
    public async Task a_declined_errand_makes_nothing_stale()
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

        var id = Guid.NewGuid();
        await _host.PutErrand(cookie, id, ErrandsApi.PlaceHold(1010), 202);
        await _host.DecideErrand(id);

        (await stream.AllOf("invalidate", TimeSpan.FromSeconds(2))).ShouldNotContain(i => ForPatron(i, patron));
    }

    [Fact] // no Wolverine, no handler: a session opened straight from the store
    public async Task a_hold_written_outside_any_handler_is_still_announced()
    {
        var patron = HoldsApi.NewPatron();
        await using var stream = await EventStream.Open(_host, await _host.SignIn(patron));
        await stream.NextOf("subscribed");

        var holdId = Guid.NewGuid();
        await using (var session = _host.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Hold>(holdId,
                new HoldRequested(holdId, patron, 1001, "CEN", DateTimeOffset.UtcNow),
                new HoldPlaced(holdId, DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        (await stream.AllOf("invalidate", TimeSpan.FromSeconds(2))).ShouldContain(i => ForPatron(i, patron));
    }
}
