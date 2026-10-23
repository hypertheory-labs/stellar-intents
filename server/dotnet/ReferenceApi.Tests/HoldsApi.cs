using Alba;
using Microsoft.Extensions.Hosting;
using ReferenceApi.Holds;
using Wolverine.Tracking;

namespace ReferenceApi.Tests;

/// <summary>Helpers shared by the tests: requests shaped the way a protocol client sends them.</summary>
public static class HoldsApi
{
    /// <summary>A patron nobody else uses, so tests never see each other's holds.</summary>
    public static string NewPatron() => $"T-{Guid.NewGuid():N}"[..12];

    /// <summary>Places a hold as <paramref name="patronId"/>, signed in (the body doesn't say whose it is).</summary>
    public static async Task<IScenarioResult> PlaceHold(this IAlbaHost host, string patronId, Guid id, PlaceHold body, int expectStatus)
    {
        var cookie = await host.SignIn(patronId);
        return await host.Scenario(s =>
        {
            s.Put.Json(body).ToUrl($"/holds/{id}");
            s.WithRequestHeader("Idempotency-Key", id.ToString());
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(expectStatus);
        });
    }

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

    /// <summary>Runs the decision now, instead of waiting for the scheduled one.</summary>
    public static Task Decide(this IHost host, Guid id) =>
        host.InvokeMessageAndWaitAsync(new EvaluateHold(id));
}
