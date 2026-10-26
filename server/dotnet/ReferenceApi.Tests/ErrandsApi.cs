using Alba;
using Edge.Errands;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace ReferenceApi.Tests;

/// <summary>Errand requests, shaped the way a protocol client sends them.</summary>
public static class ErrandsApi
{
    public static object PlaceHold(int titleId, string branchCode = "CEN") =>
        new { type = "place-hold", titleId, branchCode };

    public static object CancelHold(Guid holdId) => new { type = "cancel-hold", holdId };

    public static object SwapHold(Guid giveUp, int titleId, string branchCode = "CEN") =>
        new { type = "swap-hold", giveUp, titleId, branchCode };

    public static Task<IScenarioResult> PutErrand(this IAlbaHost host, string cookie, Guid id, object body, int expectStatus) =>
        host.Scenario(s =>
        {
            s.Put.Json(body).ToUrl($"/errands/{id}");
            s.WithRequestHeader("Idempotency-Key", id.ToString());
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(expectStatus);
        });

    public static async Task<ErrandView> GetErrand(this IAlbaHost host, string cookie, Guid id)
    {
        var result = await host.Scenario(s =>
        {
            s.Get.Url($"/errands/{id}");
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(200);
        });
        return (await result.ReadAsJsonAsync<ErrandView>())!;
    }

    public static Task<IScenarioResult> Withdraw(this IAlbaHost host, string cookie, Guid id, int expectStatus) =>
        host.Scenario(s =>
        {
            s.Delete.Url($"/errands/{id}");
            s.WithRequestHeader("Idempotency-Key", Guid.NewGuid().ToString());
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(expectStatus);
        });

    /// <summary>Closes the errand's window now, instead of waiting for the scheduled decision.</summary>
    public static Task DecideErrand(this IHost host, Guid id) =>
        host.InvokeMessageAndWaitAsync(new DecideErrand(id));
}
