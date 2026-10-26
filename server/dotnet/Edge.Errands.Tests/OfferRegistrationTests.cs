using Edge.Errands;
using Fisher;
using Microsoft.Extensions.DependencyInjection;

namespace Edge.Errands.Tests;

/// <summary>unsettled/errands.md, rule 4: an errand offers only what it was registered to offer.</summary>
public class OfferRegistrationTests
{
    private record Ask(int N);
    private record Instead(int N);
    private record Unrelated(int N);

    private class Decides<T> : IDecide<T>
    {
        public Task<Decision> Decide(T ask, Errand errand, IDocumentSession session, CancellationToken cancellation) =>
            Task.FromResult(Decision.Accept());
    }

    private static ErrandRegistry Registry(Action<ErrandOptions> configure)
    {
        var options = new ErrandOptions();
        configure(options);
        options.Validate();
        return new ErrandRegistry(options, new ServiceCollection().BuildServiceProvider());
    }

    private static ErrandRegistry AskOffersInstead() => Registry(o => o
        .Add<Ask, Decides<Ask>>("ask", offers: ["instead"])
        .Add<Instead, Decides<Instead>>("instead")
        .Add<Unrelated, Decides<Unrelated>>("unrelated"));

    [Fact]
    public void an_offer_is_sent_as_the_person_will_send_it_back()
    {
        var offered = AskOffersInstead().Offered("ask", [new Instead(7)]);
        offered.ShouldBe(["""{"type":"instead","n":7}"""]);
    }

    [Fact]
    public void offering_a_registered_errand_it_doesnt_list_throws() =>
        Should.Throw<InvalidOperationException>(() => AskOffersInstead().Offered("ask", [new Unrelated(1)]));

    [Fact]
    public void offering_something_that_isnt_an_errand_throws() =>
        Should.Throw<InvalidOperationException>(() => AskOffersInstead().Offered("ask", ["just a string"]));

    [Fact] // at startup, not the first time someone is declined
    public void listing_an_errand_nobody_registered_fails_at_registration() =>
        Should.Throw<InvalidOperationException>(() => Registry(o => o.Add<Ask, Decides<Ask>>("ask", offers: ["nope"])));
}
