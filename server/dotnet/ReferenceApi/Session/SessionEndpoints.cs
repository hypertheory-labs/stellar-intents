using System.Security.Claims;
using Wolverine.Http;

namespace ReferenceApi.Session;

/// <summary>The session as the browser sees it.</summary>
public record SessionInfo(bool IsAuthenticated, string? PatronId);

[Tags("Session")]
public static class SessionEndpoints
{
    /// <summary>
    /// Am I signed in? Always 200; the answer is in the body. A client asks this when its
    /// event stream drops, because EventSource can't tell "you were signed out" (401) from
    /// "the server went away".
    /// </summary>
    [WolverineGet("/session")]
    public static SessionInfo Get(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? new SessionInfo(true, user.PatronId())
            : new SessionInfo(false, null);
}
