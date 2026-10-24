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
    /// A GET that opens one connection's stream. <paramref name="audienceOf"/> says who the
    /// connection is for; null answers 401.
    /// </summary>
    /// <remarks>
    /// WHY the audience comes from the request (usually its user) and never from the path:
    /// who you are doesn't travel in a URL.
    /// FOR REAL: proxies and load balancers may buffer streams. Disable response buffering
    /// and compression for this route wherever it's proxied.
    /// </remarks>
    public static RouteHandlerBuilder MapServerSentEvents(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, string?> audienceOf) =>
        endpoints.MapGet(pattern, (HttpContext context, SseHub hub) =>
            audienceOf(context) is { } audience
                ? Results.ServerSentEvents(hub.Subscribe(audience, context.RequestAborted))
                : Results.Unauthorized());
}
