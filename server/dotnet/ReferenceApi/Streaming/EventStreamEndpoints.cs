using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ReferenceApi.Session;
using Wolverine.Http;

namespace ReferenceApi.Streaming;

[Tags("Events")]
public static class EventStreamEndpoints
{
    /// <summary>
    /// The signed-in patron's event stream (Server-Sent Events, PROTOCOL §3): "subscribed"
    /// first, then "intent-decided" and "invalidate" as they happen, and "heartbeat" now and
    /// then. 401 without a session.
    /// </summary>
    // WHY the patron comes from the session and not the path: identity is never in a URL.
    // FOR REAL: proxies and load balancers may buffer streams. Disable response buffering
    // and compression for this route wherever it's proxied (an edge must never buffer it).
    [Authorize]
    [WolverineGet("/events")]
    public static IResult Get(ClaimsPrincipal user, EventStreamHub hub, CancellationToken cancellation) =>
        Results.ServerSentEvents(hub.Subscribe(user.PatronId()!, cancellation));
}
