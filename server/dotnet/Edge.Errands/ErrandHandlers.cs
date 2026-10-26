using Fisher;
using Wolverine;

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
public record ErrandEnded(Guid ErrandId, string Type, string Requester, ErrandOutcome Outcome, string? Reason);

// Public only so Wolverine finds the handlers; the endpoints are the way in.
public record RequestErrand(Guid Id, string Type, string Requester, string Ask);
public enum Requested { Created, Repeated, Conflict }
public record RequestReply(Requested Result, Errand Errand);

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
        if (existing is not null)
        {
            var same = existing.Type == command.Type
                       && existing.Requester == command.Requester
                       && existing.Ask == command.Ask;
            return new RequestReply(same ? Requested.Repeated : Requested.Conflict, existing);
        }

        var now = DateTimeOffset.UtcNow;
        var requested = new ErrandRequested(command.Id, command.Type, command.Requester, command.Ask,
            now, now + registry.Get(command.Type).Window);
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
        var ended = decision switch
        {
            Decision.Declined declined => new ErrandEnded(errand.Id, errand.Type, errand.Requester, ErrandOutcome.Declined, declined.Reason),
            _ => new ErrandEnded(errand.Id, errand.Type, errand.Requester, ErrandOutcome.Accepted, null),
        };
        stream.AppendOne(decision is Decision.Declined d
            ? new ErrandDeclined(errand.Id, d.Reason, now)
            : new ErrandAccepted(errand.Id, now));
        await session.SaveChangesAsync(cancellation);

        // Cascading: sent only after the save above committed.
        messages.Add(ended);
        return messages;
    }
}

public static class WithdrawErrandHandler
{
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
