using Fisher;
using JasperFx.Events.Documents;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace ReferenceApi.Holds;

/// <summary>
/// The reads. Holds change only through errands (PUT /errands/{id}: place-hold,
/// cancel-hold), never through these URLs.
/// </summary>
[Tags("Holds")]
public static class HoldEndpoints
{
    /// <summary>One hold. Its version is sent as the ETag.</summary>
    [WolverineGet("/holds/{id}")]
    [ProducesResponseType<Hold>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public static async Task<IResult> Get(Guid id, IQuerySession session, HttpResponse response)
    {
        var hold = await session.Events.FetchLatest<Hold>(id);
        if (hold is null) return Results.NotFound();
        response.Headers.ETag = $"\"{hold.Version}\"";
        return Results.Ok(hold);
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
        return query.OrderBy(h => h.PlacedAt).ToListAsync();
    }
}
