using Alba;
using ReferenceApi.Holds;

namespace ReferenceApi.Tests;

/// <summary>PROTOCOL §1 (commands) and §2 (status checks), as the reference server does them.</summary>
public class CommandTests(AppFixture app)
{
    private readonly IAlbaHost _host = app.Host;

    [Fact]
    public async Task a_new_command_is_accepted_with_where_to_check_and_a_version()
    {
        var id = Guid.NewGuid();
        var result = await _host.PlaceHold(id, new PlaceHold(HoldsApi.NewPatron(), 1001, "CEN"), 202);

        result.Context.Response.Headers.Location.ToString().ShouldBe($"/holds/{id}");
        result.Context.Response.Headers.ETag.ToString().ShouldBe("\"1\"");
        (await result.ReadAsJsonAsync<Hold>())!.Status.ShouldBe(HoldStatus.Requested);
    }

    [Fact] // conformance 7: same id, same body -> same answer, one resource
    public async Task a_repeat_while_pending_is_202_again()
    {
        var id = Guid.NewGuid();
        var body = new PlaceHold(HoldsApi.NewPatron(), 1001, "CEN");
        await _host.PlaceHold(id, body, 202);
        await _host.PlaceHold(id, body, 202);

        var holds = await _host.GetAsJson<Hold[]>($"/holds?patronId={body.PatronId}");
        holds!.Length.ShouldBe(1);
    }

    [Fact]
    public async Task a_repeat_after_the_decision_is_200()
    {
        var id = Guid.NewGuid();
        var body = new PlaceHold(HoldsApi.NewPatron(), 1001, "CEN");
        await _host.PlaceHold(id, body, 202);
        await _host.Decide(id);

        var result = await _host.PlaceHold(id, body, 200);
        (await result.ReadAsJsonAsync<Hold>())!.Status.ShouldBe(HoldStatus.Placed);
    }

    [Fact] // conformance 7: same id, different body -> 409
    public async Task the_same_id_with_a_different_body_is_a_conflict()
    {
        var id = Guid.NewGuid();
        var patron = HoldsApi.NewPatron();
        await _host.PlaceHold(id, new PlaceHold(patron, 1001, "CEN"), 202);

        var result = await _host.PlaceHold(id, new PlaceHold(patron, 1006, "CEN"), 409);
        result.Context.Response.ContentType.ShouldBe("application/problem+json");
    }

    [Fact] // §2: not recorded yet is a 404, not an outcome
    public async Task a_status_check_for_an_unknown_id_is_404()
    {
        await _host.Scenario(s =>
        {
            s.Get.Url($"/holds/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact] // the problem's detail is written for a person (§1, problem details)
    public async Task over_the_limit_is_rejected_with_a_detail_a_person_can_read()
    {
        var patron = HoldsApi.NewPatron();
        foreach (var title in new[] { 1001, 1005, 1006 })
        {
            var placed = Guid.NewGuid();
            await _host.PlaceHold(placed, new PlaceHold(patron, title, "CEN"), 202);
            await _host.Decide(placed);
        }

        var fourth = Guid.NewGuid();
        await _host.PlaceHold(fourth, new PlaceHold(patron, 1010, "CEN"), 202);
        await _host.Decide(fourth);

        var hold = await _host.GetAsJson<Hold>($"/holds/{fourth}");
        hold!.Status.ShouldBe(HoldStatus.Rejected);
        hold.Reason.ShouldBe("You already have 3 holds, the limit.");
    }
}
