using System.Security.Claims;
using Fisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReferenceApi.Session;
using ReferenceApi.Streaming;
using JasperFx.Events.Documents;
using Wolverine;
using Wolverine.Http;

namespace ReferenceApi.Holds;

/// <summary>
/// The body of PUT /holds/{id}. The id is the client's: it is also the idempotency key.
/// Whose hold it is comes from the session, never the body (PROTOCOL §0).
/// </summary>
public record PlaceHold(int TitleId, string BranchCode);

/// <summary>Sent (scheduled) when a hold is requested; decides it.</summary>
public record EvaluateHold(Guid HoldId);

[Tags("Holds")]
public static class HoldEndpoints
{
    /// <summary>
    /// Request a hold under a client-generated id. Safe to repeat: the same id and body
    /// return the hold as it is now (202 while requested, 200 once decided); the same id
    /// with a different body (or by a different patron) is a 409. 401 without a session.
    /// </summary>
    [Authorize]
    [WolverinePut("/holds/{id}")]
    [ProducesResponseType<Hold>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Hold>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public static async Task<IResult> Put(
        Guid id,
        PlaceHold request,
        ClaimsPrincipal user,
        IDocumentSession session,
        IMessageBus bus,
        IConfiguration config,
        HttpResponse response)
    {
        var patronId = user.PatronId()!;
        var existing = await session.Events.FetchLatest<Hold>(id);
        if (existing is not null)
        {
            var same = existing.PatronId == patronId
                       && existing.TitleId == request.TitleId
                       && existing.BranchCode == request.BranchCode;
            if (!same)
                return Results.Problem(
                    detail: "A different hold already exists with this id.",
                    statusCode: StatusCodes.Status409Conflict);

            response.Headers.ETag = ETagFor(existing);
            return existing.Status == HoldStatus.Requested
                ? Results.Accepted($"/holds/{id}", existing)
                : Results.Ok(existing);
        }

        var requested = new HoldRequested(id, patronId, request.TitleId, request.BranchCode, DateTimeOffset.UtcNow);
        session.Events.StartStream<Hold>(id, requested);

        // Enlisted in the same transaction as the event (Wolverine's outbox): the hold can't
        // be stuck in "requested" because the decision message was lost between two writes.
        var delay = TimeSpan.FromSeconds(config.GetValue("Holds:DecisionDelaySeconds", 3));
        await bus.ScheduleAsync(new EvaluateHold(id), delay);

        var hold = Hold.Create(requested);
        hold.Version = 1;
        response.Headers.ETag = ETagFor(hold);
        return Results.Accepted($"/holds/{id}", hold);
    }

    /// <summary>One hold. Its version is sent as the ETag.</summary>
    [WolverineGet("/holds/{id}")]
    [ProducesResponseType<Hold>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public static async Task<IResult> Get(Guid id, IQuerySession session, HttpResponse response)
    {
        var hold = await session.Events.FetchLatest<Hold>(id);
        if (hold is null) return Results.NotFound();
        response.Headers.ETag = ETagFor(hold);
        return Results.Ok(hold);
    }

    /// <summary>
    /// Cancel a hold, requested or placed. Safe to repeat: cancelling a cancelled hold is
    /// still a 204. A rejected hold was never placed, so there's nothing to cancel (409).
    /// To confirm, GET /holds/{id}: its status is "cancelled". Only the hold's own patron
    /// can cancel it (403 for anyone else); 401 without a session.
    /// </summary>
    [Authorize]
    [WolverineDelete("/holds/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public static async Task<IResult> Cancel(Guid id, ClaimsPrincipal user, IDocumentSession session)
    {
        var hold = await session.Events.FetchLatest<Hold>(id);
        if (hold is null) return Results.NotFound();
        // WHY 403 and not 404: a 404 says "no such hold", which isn't true, and a client
        // could wait for it to appear. The id is a random UUID, so saying it exists gives
        // little away.
        if (hold.PatronId != user.PatronId())
            return Results.Problem(
                detail: "This hold belongs to someone else.",
                statusCode: StatusCodes.Status403Forbidden);
        if (hold.Status == HoldStatus.Cancelled) return Results.NoContent();
        if (hold.Status == HoldStatus.Rejected)
            return Results.Problem(
                detail: "This hold was never placed, so there's nothing to cancel.",
                statusCode: StatusCodes.Status409Conflict);

        // If the decision hasn't run yet, it will find the hold cancelled and leave it alone.
        session.Events.Append(id, new HoldCancelled(id, DateTimeOffset.UtcNow));
        return Results.NoContent();
    }

    /// <summary>
    /// Holds for a patron (?patronId=) or a title's wait list (?titleId=), or both.
    /// Cancelled holds aren't listed.
    /// </summary>
    [WolverineGet("/holds")]
    public static Task<IReadOnlyList<Hold>> List(string? patronId, int? titleId, IQuerySession session)
    {
        var query = session.Query<Hold>().Where(h => h.Status != HoldStatus.Cancelled);
        if (patronId is not null) query = query.Where(h => h.PatronId == patronId);
        if (titleId is not null) query = query.Where(h => h.TitleId == titleId);
        return query.OrderBy(h => h.RequestedAt).ToListAsync();
    }

    private static string ETagFor(Hold hold) => $"\"{hold.Version}\"";
}

public static class EvaluateHoldHandler
{
    /// <summary>The library's rule, deliberately simple: at most this many active holds per patron.</summary>
    public const int HoldLimit = 3;

    public const string CancelledFirst = "You cancelled this request before it was decided.";

    // Returns protocol events (the decision, and which reads changed) as cascading
    // messages: Wolverine sends them only after this handler's transaction (the decision
    // event) commits. See Streaming/ProtocolMessages.cs for where they go.
    public static async Task<OutgoingMessages> Handle(EvaluateHold command, IDocumentSession session)
    {
        var messages = new OutgoingMessages();
        var hold = await session.Events.FetchLatest<Hold>(command.HoldId);
        if (hold is null || hold.Status is HoldStatus.Placed or HoldStatus.Rejected) return messages; // already decided (redelivery)

        // The patron cancelled it before it was decided. The place-hold intent still needs an
        // outcome, or the client waits for one forever (found by writing both intents in IRMTO
        // form: unsettled/holds-in-irmto.md).
        if (hold.Status == HoldStatus.Cancelled)
        {
            messages.Add(ProtocolEvents.IntentDecided(hold.PatronId, hold.Id, IntentOutcome.Withdrawn, CancelledFirst));
            return messages;
        }

        var activeOthers = await session.Query<Hold>()
            .Where(h => h.PatronId == hold.PatronId && h.Id != hold.Id && h.Status == HoldStatus.Placed)
            .CountAsync();

        if (activeOthers >= HoldLimit)
        {
            var reason = $"You already have {HoldLimit} holds, the limit.";
            session.Events.Append(hold.Id, new HoldRejected(hold.Id, reason, DateTimeOffset.UtcNow));
            // The hold's id is the intent's id: the client generated it (PROTOCOL §1).
            messages.Add(ProtocolEvents.IntentDecided(hold.PatronId, hold.Id, IntentOutcome.Declined, reason));
        }
        else
        {
            session.Events.Append(hold.Id, new HoldPlaced(hold.Id, DateTimeOffset.UtcNow));
            messages.Add(ProtocolEvents.IntentDecided(hold.PatronId, hold.Id, IntentOutcome.Accepted));
        }

        return messages;
    }
}
