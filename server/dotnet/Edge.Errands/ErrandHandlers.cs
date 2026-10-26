using Fisher;
using JasperFx;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Edge.Errands;

/// <summary>
/// An errand ended. Published after the transaction that ended it commits. The app
/// handles it to tell the person: over a stream, by their channels, or both.
/// </summary>
/// <remarks>
/// WHY a message the app handles, and not a call into Edge.Sse: the pieces meet through
/// messages and HTTP, never by importing each other (README, "Rules this repo keeps").
/// The library knows *when* to tell; the app knows *how*.
/// </remarks>
public record ErrandEnded(
    Guid ErrandId, string Type, string Requester, ErrandOutcome Outcome, string? Reason, Guid? Made = null,
    ErrandOffers? Offers = null);

// Public only so Wolverine finds the handlers; the endpoints are the way in.
public record RequestErrand(Guid Id, string Type, string Requester, string Ask);
public enum Requested { Created, Repeated, Conflict, AlreadyChosen, NotOffered, NotYours }
/// <param name="Errand">Null when nothing was created or found (NotOffered, NotYours).</param>
public record RequestReply(Requested Result, Errand? Errand);

public record WithdrawErrand(Guid Id, string Requester);
public enum Withdrawal { Withdrawn, AlreadyWithdrawn, AlreadyDecided, NotFound, NotYours }

/// <summary>Sent (scheduled) when an errand is requested, for when its window closes.</summary>
public record DecideErrand(Guid Id);

// WHY each handler saves its own session: an app may or may not have Wolverine apply
// transactions automatically, and the library shouldn't require either. The session is
// Wolverine's, so the messages sent or scheduled before saving commit with it.

public static class RequestErrandHandler
{
    public static async Task<RequestReply> Handle(
        RequestErrand command, IDocumentSession session, IMessageContext bus, ErrandRegistry registry)
    {
        var existing = await session.Events.FetchLatest<Errand>(command.Id);
        // Offered: this id was given out by a decline, for one of its asks.
        var offer = await session.LoadAsync<ErrandOffer>(command.Id);
        if (existing is not null)
        {
            var same = existing.Type == command.Type
                       && existing.Requester == command.Requester
                       && existing.Ask == command.Ask;
            if (same) return new RequestReply(Requested.Repeated, existing);
            return new RequestReply(offer is null ? Requested.Conflict : Requested.AlreadyChosen, existing);
        }
        if (offer is not null)
        {
            if (offer.Requester != command.Requester) return new RequestReply(Requested.NotYours, null);
            if (!offer.Choose.Contains(ErrandRegistry.WithType(command.Type, command.Ask)))
                return new RequestReply(Requested.NotOffered, null);
        }

        var now = DateTimeOffset.UtcNow;
        var requested = new ErrandRequested(command.Id, command.Type, command.Requester, command.Ask,
            now, now + registry.Get(command.Type).Window, offer?.From);
        session.Events.StartStream<Errand>(command.Id, requested);
        await bus.ScheduleAsync(new DecideErrand(command.Id), requested.DecideAt);
        await session.SaveChangesAsync();

        var errand = Errand.Create(requested);
        errand.Version = 1;
        return new RequestReply(Requested.Created, errand);
    }
}

public static class DecideErrandHandler
{
    // WHY: a withdraw, or a second delivery, can commit between this handler's read and its
    // save. The save then fails on the version, and the retry finds the errand ended.
    // Declared here so the library doesn't depend on the app's error policies.
    public static void Configure(HandlerChain chain) => chain.OnException<ConcurrencyException>().RetryTimes(3);

    public static async Task<OutgoingMessages> Handle(
        DecideErrand command, IDocumentSession session, ErrandRegistry registry, CancellationToken cancellation)
    {
        var messages = new OutgoingMessages();
        // WHY for writing: if a withdraw commits between this read and this save, the save
        // fails on the version, Wolverine retries, and the retry finds it withdrawn.
        var stream = await session.Events.FetchForWriting<Errand>(command.Id, cancellation);
        var errand = stream.Aggregate;
        // Already ended: withdrawn first, or this is a redelivery.
        if (errand is null || !errand.Pending) return messages;

        var decision = await registry.Decide(errand, session, cancellation);
        var now = DateTimeOffset.UtcNow;
        var (ended, happened) = decision switch
        {
            Decision.Declined d => Declined(d),
            Decision.Accepted a => (
                new ErrandEnded(errand.Id, errand.Type, errand.Requester, ErrandOutcome.Accepted, null, a.Made),
                new ErrandAccepted(errand.Id, now, a.Made)),
            _ => throw new InvalidOperationException($"Unknown decision: {decision}"),
        };
        stream.AppendOne(happened);
        await session.SaveChangesAsync(cancellation);

        // Cascading: sent only after the save above committed.
        messages.Add(ended);
        return messages;

        (ErrandEnded, object) Declined(Decision.Declined d)
        {
            // One id for all of them: taking one spends it (unsettled/errands.md, rule 4).
            var offers = d.Offers.Count == 0
                ? null
                : new ErrandOffers(Guid.CreateVersion7(), registry.Offered(errand.Type, d.Offers));
            if (offers is not null)
                session.Store(new ErrandOffer
                {
                    Id = offers.Id, From = errand.Id, Requester = errand.Requester, Choose = [.. offers.Choose],
                });
            return (
                new ErrandEnded(errand.Id, errand.Type, errand.Requester, ErrandOutcome.Declined, d.Reason, Offers: offers),
                new ErrandDeclined(errand.Id, d.Reason, now, offers));
        }
    }
}

public static class WithdrawErrandHandler
{
    // WHY: the same race from the other side; the retry finds it decided, and answers 409.
    public static void Configure(HandlerChain chain) => chain.OnException<ConcurrencyException>().RetryTimes(3);

    public static async Task<Withdrawal> Handle(WithdrawErrand command, IDocumentSession session, IMessageContext bus)
    {
        var stream = await session.Events.FetchForWriting<Errand>(command.Id);
        var errand = stream.Aggregate;
        if (errand is null) return Withdrawal.NotFound;
        if (errand.Requester != command.Requester) return Withdrawal.NotYours;
        if (errand.Outcome == ErrandOutcome.Withdrawn) return Withdrawal.AlreadyWithdrawn;
        // Rule 5: only before the decision. After it, changing things is a new errand.
        if (!errand.Pending) return Withdrawal.AlreadyDecided;

        stream.AppendOne(new ErrandWithdrawn(errand.Id, DateTimeOffset.UtcNow));
        await bus.PublishAsync(new ErrandEnded(errand.Id, errand.Type, errand.Requester, ErrandOutcome.Withdrawn, null));
        await session.SaveChangesAsync();
        return Withdrawal.Withdrawn;
    }
}
