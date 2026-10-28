using JasperFx;
using JasperFx.Events.Documents;
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
    ErrandOffers? Offers = null)
{
    /// <summary>This ending as PROTOCOL §3's "intent-decided": the app only says who to tell.</summary>
    public IntentDecided ToIntentDecided() => new(ErrandId,
        Outcome switch
        {
            ErrandOutcome.Accepted => "accepted",
            ErrandOutcome.Declined => "declined",
            _ => "withdrawn",
        },
        Reason, Made, OfferView.ListOf(Offers));
}

/// <summary>PROTOCOL §3's "intent-decided" body: how an errand ended, for the person who asked.</summary>
/// <remarks>
/// WHY here, and not in each app: its fields are an errand's, and an app copying them gets
/// them wrong quietly (the empty list of offers, the outcome's spelling).
/// </remarks>
/// <param name="IntentId">The errand's id, which is also its Idempotency-Key.</param>
/// <param name="Outcome">
/// "accepted", "declined", or "withdrawn". Final: an outcome never changes. Withdrawn means
/// the person took it back before the decision: not a decision against them, so the client
/// owes no apology.
/// </param>
/// <param name="Reason">Why it was declined. Shown to the person, so written for one.</param>
/// <param name="Made">Accepted, and it made something with its own id: memory will contain it.</param>
/// <param name="Offers">Declined, and what they can choose instead. Empty when none.</param>
public record IntentDecided(Guid IntentId, string Outcome, string? Reason, Guid? Made, IReadOnlyList<OfferView> Offers)
{
    /// <summary>The event's name on the stream.</summary>
    public const string EventType = "intent-decided";
}

// Public only so Wolverine finds the handlers; the endpoints are the way in.
public record RequestErrand(Guid Id, string Type, string Requester, string Ask);
public enum Requested { Created, Repeated, Conflict, AlreadyChosen, NotOffered, NotYours }
/// <param name="Errand">Null when nothing was created or found (NotOffered, NotYours).</param>
public record RequestReply(Requested Result, Errand? Errand);

public record ReadErrand(Guid Id);
/// <param name="Errand">Null when there's no such errand.</param>
public record ReadReply(Errand? Errand);

public record WithdrawErrand(Guid Id, string Requester);
public enum Withdrawal { Withdrawn, AlreadyWithdrawn, AlreadyDecided, NotFound, NotYours }

/// <summary>Sent (scheduled) when an errand is requested, for when its window closes.</summary>
public record DecideErrand(Guid Id);

// WHY each handler saves its own session: an app may or may not have Wolverine apply
// transactions automatically, and the library shouldn't require either. The session is
// Wolverine's, so the messages sent or scheduled before saving commit with it.
//
// WHY JasperFx's interfaces, and not Fisher's IDocumentSession: so the same handlers run
// on Marten, Fisher, or Polecat. No one interface has events, loading, and saving, so a
// handler that needs more than one asks for each; Wolverine passes the same session as
// all of them.

public static class RequestErrandHandler
{
    public static async Task<RequestReply> Handle(
        RequestErrand command, IDocumentSessionOperations session, IDocumentReadOperations documents,
        IMessageContext bus, ErrandRegistry registry)
    {
        var existing = await session.Events.FetchLatest<Errand>(command.Id);
        // Offered: this id was given out by a decline, for one of its asks.
        var offer = await documents.LoadAsync<ErrandOffer>(command.Id);
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

public static class ReadErrandHandler
{
    // WHY a writing session for a read: JasperFx's read-only events interface
    // (IQueryEventStore) has no FetchLatest. Nothing is written, so saving does nothing.
    public static async Task<ReadReply> Handle(ReadErrand command, IDocumentSessionOperations session) =>
        new(await session.Events.FetchLatest<Errand>(command.Id));
}

public static class DecideErrandHandler
{
    // WHY: a withdraw, or a second delivery, can commit between this handler's read and its
    // save. The save then fails on the version, and the retry finds the errand ended.
    // Declared here so the library doesn't depend on the app's error policies.
    public static void Configure(HandlerChain chain) => chain.OnException<ConcurrencyException>().RetryTimes(3);

    public static async Task<OutgoingMessages> Handle(
        DecideErrand command, IDocumentSessionOperations session, IDocumentReadOperations documents,
        IDocumentWriteOperations writes, ErrandRegistry registry, CancellationToken cancellation)
    {
        var messages = new OutgoingMessages();
        // WHY for writing: if a withdraw commits between this read and this save, the save
        // fails on the version, Wolverine retries, and the retry finds it withdrawn.
        var stream = await session.Events.FetchForWriting<Errand>(command.Id, cancellation);
        var errand = stream.Aggregate;
        // Already ended: withdrawn first, or this is a redelivery.
        if (errand is null || !errand.Pending) return messages;

        var decision = await registry.Decide(errand, new ErrandSession(session, documents), cancellation);
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
                writes.Store(new ErrandOffer
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

    public static async Task<Withdrawal> Handle(WithdrawErrand command, IDocumentSessionOperations session, IMessageContext bus)
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
