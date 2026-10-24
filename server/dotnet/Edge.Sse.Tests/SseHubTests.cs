using System.Net.ServerSentEvents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edge.Sse.Tests;

/// <summary>The hub on its own, where the app's lifetime can be driven by hand.</summary>
public class SseHubTests
{
    private sealed class Lifetime : IHostApplicationLifetime
    {
        public readonly CancellationTokenSource Stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => Stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => Stopping.Cancel();
    }

    [Fact] // graceful shutdown must not wait on open streams
    public async Task every_stream_ends_when_the_app_starts_stopping()
    {
        var lifetime = new Lifetime();
        var hub = new SseHub(Options.Create(new SseOptions()), lifetime);
        var stream = hub.Subscribe(["P0042"], CancellationToken.None).GetAsyncEnumerator();
        (await stream.MoveNextAsync()).ShouldBeTrue(); // the opening event

        var next = stream.MoveNextAsync().AsTask();
        lifetime.StopApplication();

        var ended = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5)));
        ended.ShouldBe(next, "the stream should end promptly");
        (await next).ShouldBeFalse("it ends normally, not with an exception");
    }

    [Fact]
    public async Task an_event_reaches_its_group_and_no_other()
    {
        var hub = NewHub();
        var pat = await Open(hub, "patron:PAT");
        var sam = await Open(hub, "patron:SAM");

        hub.Publish("patron:PAT", "hello", new { });

        (await Next(pat)).ShouldNotBeNull().EventType.ShouldBe("hello");
        (await Next(sam)).ShouldBeNull();
    }

    [Fact]
    public async Task a_connection_hears_every_group_it_was_opened_with()
    {
        var hub = NewHub();
        var stream = await Open(hub, "patron:PAT", "title:42");

        hub.Publish("patron:PAT", "first", new { });
        hub.Publish("title:42", "second", new { });

        (await Next(stream)).ShouldNotBeNull().EventType.ShouldBe("first");
        (await Next(stream)).ShouldNotBeNull().EventType.ShouldBe("second");
    }

    [Fact] // broadcasting is a group you name, not what a missing one falls back to
    public async Task everyone_is_a_group_every_connection_is_in()
    {
        var hub = NewHub();
        var pat = await Open(hub, "patron:PAT");
        var nobody = await Open(hub);

        hub.Publish(SseGroups.Everyone, "hello", new { });

        (await Next(pat)).ShouldNotBeNull().EventType.ShouldBe("hello");
        (await Next(nobody)).ShouldNotBeNull().EventType.ShouldBe("hello");
    }

    [Fact]
    public async Task a_closed_connection_leaves_its_groups()
    {
        var hub = NewHub();
        var closing = new CancellationTokenSource();
        var stream = hub.Subscribe(["patron:PAT"], closing.Token).GetAsyncEnumerator();
        await stream.MoveNextAsync(); // the opening event: it's in its groups now
        var next = stream.MoveNextAsync().AsTask();
        await closing.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(next);

        // Publishing to a stale member would fail quietly, so look inside instead.
        hub.Members("patron:PAT").ShouldBe(0);
        hub.Members(SseGroups.Everyone).ShouldBe(0);
    }

    private static SseHub NewHub() => new(Options.Create(new SseOptions()), new Lifetime());

    /// <summary>A connection in these groups, past its opening event.</summary>
    private static async Task<IAsyncEnumerator<SseItem<object>>> Open(SseHub hub, params string[] groups)
    {
        var stream = hub.Subscribe(groups, CancellationToken.None).GetAsyncEnumerator();
        (await stream.MoveNextAsync()).ShouldBeTrue();
        stream.Current.EventType.ShouldBe("subscribed");
        return stream;
    }

    /// <summary>The next event, or null if none arrives soon. Use null only as the last read.</summary>
    private static async Task<SseItem<object>?> Next(IAsyncEnumerator<SseItem<object>> stream)
    {
        var read = stream.MoveNextAsync().AsTask();
        return await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(200))) == read && await read
            ? stream.Current
            : null;
    }
}
