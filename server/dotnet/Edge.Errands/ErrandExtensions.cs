using System.Text.Json;
using Fisher;
using JasperFx.Events.Projections;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Edge.Errands;

/// <summary>An errand as the person sees it: GET /errands/{id}.</summary>
/// <param name="Outcome">Null while it's pending.</param>
/// <param name="WithdrawableUntil">Until then, DELETE takes it back for free.</param>
/// <param name="Made">What accepting it made: memory will contain this id.</param>
public record ErrandView(
    Guid Id,
    string Type,
    JsonElement Ask,
    ErrandOutcome? Outcome,
    string? Reason,
    Guid? Made,
    DateTimeOffset RequestedAt,
    DateTimeOffset WithdrawableUntil,
    DateTimeOffset? EndedAt)
{
    internal static ErrandView Of(Errand e) => new(e.Id, e.Type, JsonDocument.Parse(e.Ask).RootElement,
        e.Outcome, e.Reason, e.Made, e.RequestedAt, e.WithdrawableUntil, e.EndedAt);
}

public static class ErrandExtensions
{
    /// <summary>Registers the errand types this app runs, and stores errands with Fisher.</summary>
    public static IServiceCollection AddErrands(this IServiceCollection services, Action<ErrandOptions> configure)
    {
        var options = new ErrandOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<ErrandRegistry>();
        foreach (var type in options.Types.Values) services.AddScoped(type.DeciderType);
        services.ConfigureFisher(store => store.Projections.Snapshot<Errand>(SnapshotLifecycle.Inline));
        return services;
    }

    /// <summary>Lets Wolverine find the errand handlers.</summary>
    // WHY explicit: Wolverine only discovers handlers in the app's own assembly by default.
    public static WolverineOptions UseErrands(this WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(Errand).Assembly);
        return options;
    }

    /// <summary>
    /// PUT {prefix}/{id} asks, GET reads, DELETE withdraws. <paramref name="requesterOf"/>
    /// says who's asking, from the request; null answers 401.
    /// </summary>
    /// <remarks>
    /// WHY the id is the client's: a repeat with the same id and body is the same errand, so
    /// retrying is always safe (PROTOCOL §1). The same id with another body is a 409.
    /// </remarks>
    public static RouteGroupBuilder MapErrands(
        this IEndpointRouteBuilder endpoints, string prefix, Func<HttpContext, string?> requesterOf)
    {
        var group = endpoints.MapGroup(prefix);

        group.MapPut("/{id:guid}", async (Guid id, HttpContext context, ErrandRegistry registry, IMessageBus bus) =>
        {
            if (requesterOf(context) is not { } requester) return Results.Unauthorized();

            JsonElement body;
            try { body = (await JsonDocument.ParseAsync(context.Request.Body)).RootElement; }
            catch (JsonException) { return BadRequest("The body isn't JSON."); }

            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("type", out var typeName)
                || typeName.ValueKind != JsonValueKind.String)
                return BadRequest("The body needs a \"type\": which errand this is.");
            // Closed: an errand type nobody registered isn't something we do.
            if (!registry.TryGet(typeName.GetString()!, out var type))
                return BadRequest($"There's no errand called \"{typeName.GetString()}\".");

            string ask;
            try { ask = ErrandRegistry.Canonical(type, body); }
            catch (JsonException) { return BadRequest($"That isn't a \"{type.Name}\" errand."); }

            var reply = await bus.InvokeAsync<RequestReply>(new RequestErrand(id, type.Name, requester, ask));
            var view = ErrandView.Of(reply.Errand);
            context.Response.Headers.ETag = $"\"{reply.Errand.Version}\"";
            return reply.Result switch
            {
                Requested.Conflict => Results.Problem(
                    detail: "A different errand already exists with this id.",
                    statusCode: StatusCodes.Status409Conflict),
                _ when reply.Errand.Pending => Results.Accepted($"{prefix}/{id}", view),
                _ => Results.Ok(view),
            };
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext context, IQuerySession session) =>
        {
            if (requesterOf(context) is not { } requester) return Results.Unauthorized();
            var errand = await session.Events.FetchLatest<Errand>(id);
            if (errand is null) return Results.NotFound();
            // WHY 403 and not 404: a 404 says there's no such errand, which isn't true.
            if (errand.Requester != requester) return Forbidden();
            context.Response.Headers.ETag = $"\"{errand.Version}\"";
            return Results.Ok(ErrandView.Of(errand));
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext context, IMessageBus bus) =>
        {
            if (requesterOf(context) is not { } requester) return Results.Unauthorized();
            return await bus.InvokeAsync<Withdrawal>(new WithdrawErrand(id, requester)) switch
            {
                Withdrawal.NotFound => Results.NotFound(),
                Withdrawal.NotYours => Forbidden(),
                Withdrawal.AlreadyDecided => Results.Problem(
                    detail: "This was already decided, so it can't be taken back. Changing it now is a new request.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.NoContent(),
            };
        });

        return group;
    }

    private static IResult BadRequest(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Forbidden() =>
        Results.Problem(detail: "This errand is someone else's.", statusCode: StatusCodes.Status403Forbidden);
}
