using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ReferenceApi.Session;

/*
  Who the user is: a cookie session (PROTOCOL: Identity, proposed).

  WHY a cookie: the browser sends it on its own, including on EventSource connections, which
  can't send an Authorization header. Identity is never in a URL.

  FOR REAL: this cookie comes from an edge (a BFF) that signed the user in with an OIDC
  provider and keeps their tokens server-side. Here, in Development, POST /dev/sign-in sets
  it directly (see DevSignIn). The rest of the app can't tell the difference: it only ever
  reads the claim.
*/
public static class SessionSetup
{
    /// <summary>The claim holding the signed-in patron's id.</summary>
    public const string PatronClaim = "patron";

    public static IServiceCollection AddCookieSession(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "stellar-session";
                options.Cookie.HttpOnly = true;
                // WHY Strict: with the required Idempotency-Key (see RequireIdempotencyKey),
                // this is the whole CSRF defence. Same-origin (an edge, or a dev proxy) is
                // what makes Strict workable.
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // http in dev

                // WHY 401 and not the cookie handler's default redirect to a login page: a
                // fetch or an EventSource can't follow a login page. The client asks
                // GET /session to find out why it was turned away.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorization();
        return services;
    }

    public static string? PatronId(this ClaimsPrincipal user) => user.FindFirstValue(PatronClaim);
}
