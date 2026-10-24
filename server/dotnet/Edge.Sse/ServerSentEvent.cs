using Wolverine;

namespace Edge.Sse;

/// <summary>
/// An event for the open streams in a group. Return it from a
/// Wolverine handler, or publish it on the handler's bus, and it goes down the stream only
/// after the handler's transaction commits.
/// </summary>
/// <remarks>
/// WHY a message, and not a call to the hub: the hub would send at once, so a browser could
/// hear about a change that then rolls back. As a cascading message, it's sent only after
/// commit. That guarantee is the only reason this package needs Wolverine.
/// </remarks>
/// <param name="Group">Which open streams get it. <see cref="SseGroups.Everyone"/> reaches all of them.</param>
/// <param name="EventType">The SSE event name, which is what EventSource listeners subscribe to.</param>
/// <param name="Data">Serialized as JSON.</param>
public record ServerSentEvent(string Group, string EventType, object Data);

/// <summary>Hands committed events to the hub.</summary>
public static class ServerSentEventHandler
{
    public static void Handle(ServerSentEvent sent, SseHub hub) => hub.Publish(sent.Group, sent.EventType, sent.Data);
}

public static class SseWolverineExtensions
{
    /// <summary>Lets this app's handlers send <see cref="ServerSentEvent"/>s.</summary>
    // WHY explicit: Wolverine only discovers handlers in the app's own assembly by default.
    public static WolverineOptions UseServerSentEvents(this WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(ServerSentEvent).Assembly);
        return options;
    }
}
