using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edge.Sse;

/// <summary>
/// Fans events out to open connections, by group. A connection is in every group it was
/// opened with, and in <see cref="SseGroups.Everyone"/>.
/// </summary>
/// <remarks>
/// WHY one channel PER CONNECTION, not one shared channel: .NET channels have competing
/// readers. Each item is read by exactly one reader, so two connections (two tabs) reading
/// one channel would split the events between them. So each connection gets its own
/// channel, and Publish copies an event into every channel it's meant for.
///
/// WHY bounded, and WHY "complete the channel" when it's full: a connection that stops
/// reading (a frozen tab) must not grow memory forever. When its channel is full we end
/// that stream instead of dropping events silently. The browser reconnects and gets the
/// opening event again, which tells it to re-check. A dropped connection is survivable; a
/// silently dropped event isn't.
///
/// FOR REAL:
/// - Per-process. With more than one instance, every instance has to see every event: a
///   backplane (a Wolverine transport every instance listens to). The hub's shape
///   survives; it would just be fed from that.
/// - No replay, deliberately: the opening event stands in for it.
///
/// WHY end every stream when the app starts stopping: an open stream is a request in
/// flight, so graceful shutdown waits for it (30 s by default) while heartbeats carry on.
/// During a rolling deploy, clients would sit on a dying instance for that long. Ending
/// the streams lets them reconnect at once, to an instance that's staying.
/// </remarks>
public sealed class SseHub(IOptions<SseOptions> options, IHostApplicationLifetime lifetime)
{
    private readonly SseOptions _options = options.Value;

    // group -> (connectionId -> that connection's channel)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<SseItem<object>>>> _byGroup = new();

    /// <summary>One connection's stream. Ends when the client disconnects.</summary>
    public async IAsyncEnumerable<SseItem<object>> Subscribe(
        IEnumerable<string> groups,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        var channel = Channel.CreateBounded<SseItem<object>>(new BoundedChannelOptions(_options.PerConnectionCapacity)
        {
            SingleReader = true, // only this connection reads it
            SingleWriter = false, // events arrive from any handler thread, heartbeats from a timer
        });

        var connectionId = Guid.NewGuid();
        // WHY everyone is a group like any other: broadcasting is then a choice a caller
        // makes by name, never what a missing audience falls back to.
        var memberOf = groups.Append(SseGroups.Everyone).Distinct().ToArray();
        foreach (var group in memberOf) _byGroup.GetOrAdd(group, _ => new())[connectionId] = channel;
        using var heartbeats = StartHeartbeats(channel, cancellation);
        // The app is stopping: end this stream normally (see remarks). Completing the
        // channel ends the read loop below without an exception.
        using var stopping = lifetime.ApplicationStopping.Register(() => channel.Writer.TryComplete());

        try
        {
            // First on every connection: "you're live; anything that happened while you were
            // away is not coming down this pipe, so re-check now."
            if (_options.OpeningEvent is { } opening) yield return new SseItem<object>(new { }, opening);

            await foreach (var item in channel.Reader.ReadAllAsync(cancellation))
                // No EventId: there's no replay, so an id would promise a resume we can't keep.
                yield return item;
        }
        finally
        {
            foreach (var group in memberOf)
                if (_byGroup.TryGetValue(group, out var connections)) connections.TryRemove(connectionId, out _);
            // FOR REAL: remove a group's entry when it empties (carefully: racing a new subscriber).
        }
    }

    /// <summary>Copies an event to every open connection in the group.</summary>
    public void Publish(string group, string eventType, object data)
    {
        if (!_byGroup.TryGetValue(group, out var connections)) return;
        var item = new SseItem<object>(data, eventType);
        foreach (var channel in connections.Values) Offer(channel, item);
    }

    /// <summary>How many open connections are in the group. For tests.</summary>
    internal int Members(string group) => _byGroup.TryGetValue(group, out var connections) ? connections.Count : 0;

    // WHY a "heartbeat" event and not a comment line (": keep-alive"): .NET's SseItem has no
    // way to write a comment. An event nobody listens for is just as invisible to
    // EventSource (named events only reach listeners for that name).
    private Timer StartHeartbeats(Channel<SseItem<object>> channel, CancellationToken cancellation) =>
        new(_ =>
        {
            if (!cancellation.IsCancellationRequested) Offer(channel, new SseItem<object>(new { }, "heartbeat"));
        }, null, _options.Heartbeat, _options.Heartbeat);

    private static void Offer(Channel<SseItem<object>> channel, SseItem<object> item)
    {
        // Full: this connection isn't keeping up. End its stream (see remarks).
        if (!channel.Writer.TryWrite(item)) channel.Writer.TryComplete();
    }
}
