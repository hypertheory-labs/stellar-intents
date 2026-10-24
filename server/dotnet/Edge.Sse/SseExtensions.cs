using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Edge.Sse;

public static class SseExtensions
{
    /// <summary>Registers the hub that open streams read from.</summary>
    public static IServiceCollection AddServerSentEvents(this IServiceCollection services, Action<SseOptions>? configure = null)
    {
        services.AddOptions<SseOptions>().Configure(options => configure?.Invoke(options));
        services.AddSingleton<SseHub>();
        return services;
    }

    /// <summary>
    /// A GET that opens one connection's stream. <paramref name="groupsOf"/> says which groups
    /// the connection is in (besides <see cref="SseGroups.Everyone"/>); null answers 401.
    /// </summary>
    /// <remarks>
    /// WHY the app decides the groups: the request can ask (a query string of topics, as
    /// Mercure does), but only the app can say what this person may hear. Who they are comes
    /// from the request's identity, never from the URL.
    /// FOR REAL: proxies and load balancers may buffer streams. Disable response buffering
    /// and compression for this route wherever it's proxied.
    /// </remarks>
    public static RouteHandlerBuilder MapServerSentEvents(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, IEnumerable<string>?> groupsOf) =>
        endpoints.MapGet(pattern, (HttpContext context, SseHub hub) =>
            groupsOf(context) is { } groups
                ? Results.ServerSentEvents(hub.Subscribe(groups, context.RequestAborted))
                : Results.Unauthorized());
}
