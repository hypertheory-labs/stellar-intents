using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using static Edge.Errands.StoreTests.ErrandStore;

namespace Edge.Errands.StoreTests;

/// <summary>
/// What Edge.Errands promises that depends on the store underneath. Each store's test
/// project runs all of these, so "runs on Marten" is a test result, not a compile result.
/// </summary>
public abstract class ErrandStoreTests<TStore>(TStore store) : IClassFixture<TStore> where TStore : ErrandStore
{
    private readonly IMessageBus _bus = store.Host.Services.GetRequiredService<IMessageBus>();
    private const string Someone = "someone";

    private Task<RequestReply> Request(Guid id, string type, string ask, string requester = Someone) =>
        _bus.InvokeAsync<RequestReply>(new RequestErrand(id, type, requester, ask));

    private Task Decide(Guid id) => _bus.InvokeAsync(new DecideErrand(id));

    private async Task<Errand> Read(Guid id) => (await _bus.InvokeAsync<ReadReply>(new ReadErrand(id))).Errand!;

    private async Task<int> EventsIn(Guid stream) =>
        (await store.Sessions.QuerySession().Events.FetchStreamAsync(stream)).Count;

    [Fact]
    public async Task a_new_errand_is_pending_until_it_is_decided()
    {
        var id = Guid.NewGuid();
        var reply = await Request(id, "want", Ask(new Want(1)));

        reply.Result.ShouldBe(Requested.Created);
        (await Read(id)).Pending.ShouldBeTrue();
    }

    [Fact]
    public async Task the_same_ask_again_is_a_repeat_and_another_is_a_conflict()
    {
        var id = Guid.NewGuid();
        await Request(id, "want", Ask(new Want(1)));

        (await Request(id, "want", Ask(new Want(1)))).Result.ShouldBe(Requested.Repeated);
        (await Request(id, "want", Ask(new Want(2)))).Result.ShouldBe(Requested.Conflict);
    }

    [Fact] // the decider's writes and the outcome are one transaction
    public async Task what_a_decider_makes_is_saved_with_the_outcome()
    {
        var id = Guid.NewGuid();
        var made = Guid.NewGuid();
        await Request(id, "make", Ask(new Make(made)));
        await Decide(id);

        var errand = await Read(id);
        errand.Outcome.ShouldBe(ErrandOutcome.Accepted);
        errand.Made.ShouldBe(made);
        (await EventsIn(made)).ShouldBe(1);
    }

    [Fact] // a redelivered decision changes nothing
    public async Task deciding_twice_decides_once()
    {
        var id = Guid.NewGuid();
        await Request(id, "make", Ask(new Make(Guid.NewGuid())));
        await Decide(id);
        await Decide(id);

        (await EventsIn(id)).ShouldBe(2);
    }

    [Fact] // with no window it's decided at once, so deciding it here too races that
    public async Task deciding_while_it_is_being_decided_does_not_fail()
    {
        for (var tries = 0; tries < 20; tries++)
        {
            var id = Guid.NewGuid();
            await Request(id, "now", Ask(new Now()));
            // The loser fails on the version, and the retry finds the errand ended.
            await Decide(id);

            (await Read(id)).Outcome.ShouldBe(ErrandOutcome.Declined);
            // Requested and one outcome: the loser's outcome was never stored.
            (await EventsIn(id)).ShouldBe(2);
        }
    }

    [Fact]
    public async Task withdrawn_before_the_decision_it_is_never_decided()
    {
        var id = Guid.NewGuid();
        var made = Guid.NewGuid();
        await Request(id, "make", Ask(new Make(made)));

        (await _bus.InvokeAsync<Withdrawal>(new WithdrawErrand(id, Someone))).ShouldBe(Withdrawal.Withdrawn);
        await Decide(id);

        (await Read(id)).Outcome.ShouldBe(ErrandOutcome.Withdrawn);
        (await EventsIn(made)).ShouldBe(0);
    }

    [Fact]
    public async Task after_the_decision_it_cant_be_withdrawn()
    {
        var id = Guid.NewGuid();
        await Request(id, "make", Ask(new Make(Guid.NewGuid())));
        await Decide(id);

        (await _bus.InvokeAsync<Withdrawal>(new WithdrawErrand(id, Someone))).ShouldBe(Withdrawal.AlreadyDecided);
    }

    [Fact] // ErrandOffer is stored, and read back, without a table made on demand
    public async Task a_decline_offers_and_taking_one_remembers_where_it_came_from()
    {
        var id = Guid.NewGuid();
        await Request(id, "want", Ask(new Want(1)));
        await Decide(id);

        var declined = await Read(id);
        declined.Outcome.ShouldBe(ErrandOutcome.Declined);
        var offers = declined.Offers.ShouldNotBeNull();

        var taken = await Request(offers.Id, "instead", Ask(new Instead(2)));
        taken.Result.ShouldBe(Requested.Created);
        taken.Errand!.OfferedBy.ShouldBe(id);
        (await Request(offers.Id, "instead", Ask(new Instead(2)), requester: "someone else"))
            .Result.ShouldBe(Requested.AlreadyChosen);
    }
}
