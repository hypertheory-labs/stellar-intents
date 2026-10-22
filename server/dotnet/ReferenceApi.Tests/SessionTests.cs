using Alba;
using ReferenceApi.Holds;
using ReferenceApi.Session;

namespace ReferenceApi.Tests;

/// <summary>Identity (PROTOCOL, proposed): a cookie session, and how a client asks about it.</summary>
public class SessionTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    [Fact]
    public async Task without_a_session_the_answer_is_signed_out_not_an_error()
    {
        var session = await _host.GetAsJson<SessionInfo>("/session");
        session!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task signed_in_the_session_names_the_patron()
    {
        var cookie = await _host.SignIn("P0042");

        var result = await _host.Scenario(s =>
        {
            s.Get.Url("/session");
            s.WithRequestHeader("Cookie", cookie);
        });
        (await result.ReadAsJsonAsync<SessionInfo>()).ShouldBe(new SessionInfo(true, "P0042"));
    }

    [Fact]
    public async Task the_session_cookie_is_strict_and_out_of_reach_of_scripts()
    {
        var result = await _host.Scenario(s =>
        {
            s.Post.Url("/dev/sign-in?patronId=P0042");
            s.StatusCodeShouldBe(204);
        });
        var setCookie = result.Context.Response.Headers.SetCookie.ToString().ToLowerInvariant();

        setCookie.ShouldContain("samesite=strict");
        setCookie.ShouldContain("httponly");
    }

    [Fact] // the CSRF defence: an unsafe request must carry its Idempotency-Key
    public async Task a_command_without_an_idempotency_key_is_turned_away()
    {
        var result = await _host.Scenario(s =>
        {
            s.Put.Json(new PlaceHold(HoldsApi.NewPatron(), 1001, "CEN")).ToUrl($"/holds/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(400);
        });
        result.Context.Response.ContentType.ShouldBe("application/problem+json");
    }
}
