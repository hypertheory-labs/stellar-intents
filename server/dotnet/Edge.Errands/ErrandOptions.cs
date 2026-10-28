using System.Text.Json;
using System.Text.Json.Nodes;
using JasperFx.Events;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Edge.Errands;

/// <summary>Decides one type of errand. The app writes one per errand type.</summary>
/// <typeparam name="TAsk">What the person asks for: the request body.</typeparam>
public interface IDecide<in TAsk>
{
    /// <summary>
    /// Accept or decline. Make the change in <paramref name="session"/>: it's committed in
    /// the same transaction as the outcome, so the two can't disagree.
    /// </summary>
    Task<Decision> Decide(TAsk ask, Errand errand, ErrandSession session, CancellationToken cancellation);
}

/// <summary>The session a decision is made in, whichever store it is (Marten, Fisher, Polecat).</summary>
/// <remarks>
/// WHY a type of our own: JasperFx's interfaces split one session across several, and
/// none has both events and queries. This gives a decider the parts deciders have needed,
/// and no more: add one when a decider needs it.
/// </remarks>
public sealed class ErrandSession(IDocumentSessionOperations session, IDocumentReadOperations documents)
{
    /// <summary>Fetch, append, and start streams. Saved with the outcome.</summary>
    public IEventStoreOperations Events => session.Events;

    public IQueryable<T> Query<T>() => documents.Query<T>();
}

/// <summary>The errand types this app runs.</summary>
public sealed class ErrandOptions
{
    internal Dictionary<string, ErrandType> Types { get; } = new();

    /// <summary>Registers an errand type.</summary>
    /// <param name="type">Its name on the wire: the body's "type".</param>
    /// <param name="window">
    /// How long the decision waits, so the person can take it back for free. Zero decides
    /// as soon as it can.
    /// </param>
    /// <param name="offers">
    /// The errand types a decline may offer instead (closed: nothing else is offered).
    /// </param>
    public ErrandOptions Add<TAsk, TDecider>(string type, TimeSpan? window = null, IEnumerable<string>? offers = null)
        where TDecider : class, IDecide<TAsk>
    {
        Types[type] = new ErrandType(type, typeof(TAsk), typeof(TDecider), window ?? TimeSpan.Zero,
            (offers ?? []).ToHashSet(),
            (decider, ask, errand, session, cancellation) =>
                ((IDecide<TAsk>)decider).Decide((TAsk)ask, errand, session, cancellation));
        return this;
    }

    // WHY at startup: an offer naming an errand nobody registered would only fail when
    // someone was declined, which is the worst moment to find out.
    internal void Validate()
    {
        foreach (var type in Types.Values)
        foreach (var offered in type.Offers.Where(o => !Types.ContainsKey(o)))
            throw new InvalidOperationException($"\"{type.Name}\" offers \"{offered}\", which isn't a registered errand.");
    }
}

internal sealed record ErrandType(
    string Name,
    Type AskType,
    Type DeciderType,
    TimeSpan Window,
    IReadOnlySet<string> Offers,
    Func<object, object, Errand, ErrandSession, CancellationToken, Task<Decision>> Decide);

/// <summary>Looks up registered errand types, and reads and writes their asks.</summary>
public sealed class ErrandRegistry(ErrandOptions options, IServiceProvider services)
{
    // The ask is stored as this JSON, so a repeat compares equal however it was formatted.
    internal static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    internal bool TryGet(string name, out ErrandType type) => options.Types.TryGetValue(name, out type!);

    internal ErrandType Get(string name) => options.Types[name];

    /// <summary>The ask as its type, then back to JSON in one canonical form. Throws if it doesn't fit.</summary>
    internal static string Canonical(ErrandType type, JsonElement body)
    {
        var ask = body.Deserialize(type.AskType, Json) ?? throw new JsonException("The body is empty.");
        return JsonSerializer.Serialize(ask, type.AskType, Json);
    }

    /// <summary>An ask as the person sends it: its JSON, with "type" first.</summary>
    internal static string WithType(string type, string ask)
    {
        var sent = new JsonObject { ["type"] = type };
        foreach (var (name, value) in JsonNode.Parse(ask)!.AsObject()) sent[name] = value?.DeepClone();
        return sent.ToJsonString(Json);
    }

    /// <summary>A decline's offers, as the person will send one back. Throws if <paramref name="from"/> may not offer one.</summary>
    internal IReadOnlyList<string> Offered(string from, IReadOnlyList<object> offers)
    {
        var allowed = Get(from).Offers;
        return [.. offers.Select(offer =>
        {
            var type = options.Types.Values.FirstOrDefault(t => t.AskType == offer.GetType() && allowed.Contains(t.Name))
                ?? throw new InvalidOperationException(
                    $"\"{from}\" offered a {offer.GetType().Name}, which isn't one of the errands it was registered to offer.");
            return WithType(type.Name, JsonSerializer.Serialize(offer, type.AskType, Json));
        })];
    }

    internal async Task<Decision> Decide(Errand errand, ErrandSession session, CancellationToken cancellation)
    {
        var type = Get(errand.Type);
        var ask = JsonSerializer.Deserialize(errand.Ask, type.AskType, Json)!;
        // WHY a scope per decision: a decider may depend on scoped services.
        await using var scope = services.CreateAsyncScope();
        var decider = scope.ServiceProvider.GetRequiredService(type.DeciderType);
        return await type.Decide(decider, ask, errand, session, cancellation);
    }
}
