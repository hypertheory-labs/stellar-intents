using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ReferenceApi.Session;

/// <summary>
/// POST /dev/sign-in?patronId=P0042 and POST /dev/sign-out: a session without an identity
/// provider.
/// </summary>
/// <remarks>
/// WHY: so the protocol can be tried with nothing but <c>dotnet run</c>. The profile (an edge
/// with a real OIDC provider) replaces this; nothing downstream changes, because everything
/// reads the claim, not how it got there.
///
/// FOR REAL: anyone can be anyone. Like /dev/reset, it's mapped only in Development, as a
/// minimal API, so the environment check is the only thing that decides whether it exists.
/// </remarks>
public static class DevSignIn
{
    public static void MapDevSignIn(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        app.MapPost("/dev/sign-in", async (string patronId, HttpContext context) =>
            {
                var identity = new ClaimsIdentity(
                    [new Claim(SessionSetup.PatronClaim, patronId)],
                    CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(new ClaimsPrincipal(identity));
                return Results.NoContent();
            })
            .ExcludeFromDescription();

        app.MapPost("/dev/sign-out", async (HttpContext context) =>
            {
                await context.SignOutAsync();
                return Results.NoContent();
            })
            .ExcludeFromDescription();
    }
}
