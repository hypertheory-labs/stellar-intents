using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ReferenceApi.Streaming;

/// <summary>
/// Fans protocol events out to every open connection, by patron.
/// </summary>
/// <remarks>
/// WHY one channel PER CONNECTION, not one shared channel: .NET channels have competing
/// readers. Each item is read by exactly one reader, so two connections (two tabs) reading
/// one channel would split the events between them. So each connection gets its own
/// channel, and Publish copies an event into every channel it's meant for.
///
/// WHY bounded, and WHY "complete the channel" when it's full: a connection that stops
/// reading (a frozen tab) must not grow memory forever. When its channel is full we end
/// that stream instead of dropping events silently. The browser reconnects, gets
/// "subscribed" again, and re-checks (PROTOCOL §3). A dropped connection is survivable; a
/// silently dropped event isn't.
///
/// FOR REAL:
/// - Per-process. With more than one instance, every instance has to see every protocol
///   message: a backplane (a Wolverine transport every instance listens to). The hub's shape
///   survives; its handlers would just be fed from that.
/// - No replay, deliberately (PROTOCOL §3, stream rules).
/// </remarks>
public sealed class EventStreamHub(IConfiguration config)
{
    // patronId -> (connectionId -> that connection's channel)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<SseItem<object>>>> _byPatron = new();

    // Generous for one person's reads and intents. FOR REAL: size it against burst rate.
    private const int PerConnectionCapacity = 64;

    // PROTOCOL §3: about every 15-30 s, so proxies don't close an idle stream.
    private readonly TimeSpan _heartbeat = TimeSpan.FromSeconds(config.GetValue("Events:HeartbeatSeconds", 20));

    /// <summary>One connection's stream. Ends when the client disconnects.</summary>
    public async IAsyncEnumerable<SseItem<object>> Subscribe(
        string patronId,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        var channel = Channel.CreateBounded<SseItem<object>>(new BoundedChannelOptions(PerConnectionCapacity)
        {
            SingleReader = true, // only this connection reads it
            SingleWriter = false, // events arrive from any handler thread, heartbeats from a timer
        });

        var connectionId = Guid.NewGuid();
        var connections = _byPatron.GetOrAdd(patronId, _ => new());
        connections[connectionId] = channel;
        using var heartbeats = StartHeartbeats(channel, cancellation);

        try
        {
            // PROTOCOL §3: first on every connection. "You're live; anything that happened
            // while you were away is not coming down this pipe, so re-check now."
            yield return new SseItem<object>(new { }, "subscribed");

            await foreach (var item in channel.Reader.ReadAllAsync(cancellation))
                // No EventId: there's no replay, so an id would promise a resume we can't keep.
                yield return item;
        }
        finally
        {
            connections.TryRemove(connectionId, out _);
            // FOR REAL: remove the patron's entry when it empties (carefully: racing a new subscriber).
        }
    }

    /// <summary>Copies an event to every open connection for the audience (null: everyone).</summary>
    public void Publish(string? audience, string eventType, object data)
    {
        var item = new SseItem<object>(data, eventType);
        var targets = audience is null
            ? _byPatron.Values.SelectMany(c => c.Values)
            : _byPatron.TryGetValue(audience, out var connections) ? connections.Values : [];

        foreach (var channel in targets) Offer(channel, item);
    }

    // WHY a "heartbeat" event and not the comment line (": keep-alive") PROTOCOL §3 suggests:
    // .NET's SseItem has no way to write a comment. An event nobody listens for is just as
    // invisible to EventSource (named events only reach listeners for that name).
    private Timer StartHeartbeats(Channel<SseItem<object>> channel, CancellationToken cancellation) =>
        new(_ =>
        {
            if (!cancellation.IsCancellationRequested) Offer(channel, new SseItem<object>(new { }, "heartbeat"));
        }, null, _heartbeat, _heartbeat);

    private static void Offer(Channel<SseItem<object>> channel, SseItem<object> item)
    {
        // Full: this connection isn't keeping up. End its stream (see remarks).
        if (!channel.Writer.TryWrite(item)) channel.Writer.TryComplete();
    }
}
