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

    public static Task<IScenarioResult> PlaceHold(this IAlbaHost host, Guid id, PlaceHold body, int expectStatus) =>
        host.Scenario(s =>
        {
            s.Put.Json(body).ToUrl($"/holds/{id}");
            s.WithRequestHeader("Idempotency-Key", id.ToString());
            s.StatusCodeShouldBe(expectStatus);
        });

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
