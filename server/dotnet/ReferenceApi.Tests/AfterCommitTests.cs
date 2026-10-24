using Alba;
using Edge.Sse;
using Fisher;
using ReferenceApi.Streaming;
using Wolverine;

namespace ReferenceApi.Tests;

public record ChangeSomething(string PatronId, bool CommitWillFail);
public record SomethingChanged;

/// <summary>Appends an event and announces it. With CommitWillFail, the append's expected version is wrong.</summary>
public static class ChangeSomethingHandler
{
    public static ServerSentEvent Handle(ChangeSomething command, IDocumentSession session)
    {
        // A new stream is at version 0, so expecting 5 makes the commit throw, after this returns.
        if (command.CommitWillFail) session.Events.Append(Guid.NewGuid(), 5, new SomethingChanged());
        else session.Events.Append(Guid.NewGuid(), new SomethingChanged());
        return ProtocolEvents.Invalidate(ProtocolEvents.PatronGroup(command.PatronId), "/something");
    }
}

/// <summary>
/// PROTOCOL §3, "publish only after commit": the browser never hears about a change that
/// didn't happen. Here the handler succeeds and the database commit fails.
/// </summary>
public class AfterCommitTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    [Fact] // the control: proves the wait below is long enough to see an event
    public async Task a_change_that_commits_is_announced()
    {
        var patron = HoldsApi.NewPatron();
        await using var stream = await EventStream.Open(_host, await _host.SignIn(patron));
        await stream.NextOf("subscribed");

        await _host.MessageBus().InvokeAsync(new ChangeSomething(patron, CommitWillFail: false));

        (await stream.NextOf("invalidate")).GetProperty("keys")[0].GetString().ShouldBe("/something");
    }

    [Fact]
    public async Task a_change_whose_commit_fails_is_never_announced()
    {
        var patron = HoldsApi.NewPatron();
        await using var stream = await EventStream.Open(_host, await _host.SignIn(patron));
        await stream.NextOf("subscribed");

        await Should.ThrowAsync<Exception>(
            _host.MessageBus().InvokeAsync(new ChangeSomething(patron, CommitWillFail: true)));

        (await stream.AllOf("invalidate", TimeSpan.FromSeconds(2))).ShouldBeEmpty();
    }
}
