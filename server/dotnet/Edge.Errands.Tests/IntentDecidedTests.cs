using System.Text.Json;
using Edge.Errands;

namespace Edge.Errands.Tests;

/// <summary>PROTOCOL §3, intent-decided: how an errand ended, as the browser reads it.</summary>
public class IntentDecidedTests
{
    // How ASP.NET writes it down the stream (camelCase), without the app's enum converter.
    private static JsonElement OnTheWire(ErrandEnded ended) =>
        JsonSerializer.SerializeToElement(ended.ToIntentDecided(), JsonSerializerOptions.Web);

    [Fact]
    public void the_event_is_called_intent_decided() => IntentDecided.EventType.ShouldBe("intent-decided");

    [Fact]
    public void a_decline_says_why_and_has_an_empty_list_of_offers()
    {
        var id = Guid.NewGuid();
        var body = OnTheWire(new ErrandEnded(id, "place-hold", "P1", ErrandOutcome.Declined, "No."));

        body.GetProperty("intentId").GetGuid().ShouldBe(id);
        body.GetProperty("outcome").GetString().ShouldBe("declined");
        body.GetProperty("reason").GetString().ShouldBe("No.");
        body.GetProperty("offers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void an_acceptance_says_what_it_made()
    {
        var made = Guid.NewGuid();
        var body = OnTheWire(new ErrandEnded(Guid.NewGuid(), "place-hold", "P1", ErrandOutcome.Accepted, null, made));

        body.GetProperty("outcome").GetString().ShouldBe("accepted");
        body.GetProperty("made").GetGuid().ShouldBe(made);
    }

    [Fact]
    public void a_withdrawal_is_spelled_withdrawn() =>
        OnTheWire(new ErrandEnded(Guid.NewGuid(), "place-hold", "P1", ErrandOutcome.Withdrawn, null))
            .GetProperty("outcome").GetString().ShouldBe("withdrawn");

    [Fact]
    public void offers_are_the_ones_the_errand_shows()
    {
        var offers = new ErrandOffers(Guid.NewGuid(), ["{\"type\":\"swap-hold\",\"titleId\":1}"]);
        var body = OnTheWire(new ErrandEnded(Guid.NewGuid(), "place-hold", "P1", ErrandOutcome.Declined, "No.",
            Offers: offers));

        var group = body.GetProperty("offers")[0];
        group.GetProperty("id").GetGuid().ShouldBe(offers.Id);
        group.GetProperty("choose")[0].GetProperty("type").GetString().ShouldBe("swap-hold");
    }
}
