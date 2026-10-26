using Alba;

namespace ReferenceApi.Tests;

/// <summary>Helpers shared by the tests: requests shaped the way a protocol client sends them.</summary>
public static class HoldsApi
{
    /// <summary>A patron nobody else uses, so tests never see each other's holds.</summary>
    public static string NewPatron() => $"T-{Guid.NewGuid():N}"[..12];

    /// <summary>Signs in (Development only) and returns the session cookie, to send as a Cookie header.</summary>
    public static async Task<string> SignIn(this IAlbaHost host, string patronId)
    {
        var result = await host.Scenario(s =>
        {
            s.Post.Url($"/dev/sign-in?patronId={patronId}");
            s.StatusCodeShouldBe(204);
        });
        return result.Context.Response.Headers.SetCookie.ToString().Split(';')[0];
    }
}
