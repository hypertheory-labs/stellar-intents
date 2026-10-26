using System.Text.Json;
using Fisher;
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
    Task<Decision> Decide(TAsk ask, Errand errand, IDocumentSession session, CancellationToken cancellation);
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
    public ErrandOptions Add<TAsk, TDecider>(string type, TimeSpan? window = null)
        where TDecider : class, IDecide<TAsk>
    {
        Types[type] = new ErrandType(type, typeof(TAsk), typeof(TDecider), window ?? TimeSpan.Zero,
            (decider, ask, errand, session, cancellation) =>
                ((IDecide<TAsk>)decider).Decide((TAsk)ask, errand, session, cancellation));
        return this;
    }
}

internal sealed record ErrandType(
    string Name,
    Type AskType,
    Type DeciderType,
    TimeSpan Window,
    Func<object, object, Errand, IDocumentSession, CancellationToken, Task<Decision>> Decide);

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

    internal async Task<Decision> Decide(Errand errand, IDocumentSession session, CancellationToken cancellation)
    {
        var type = Get(errand.Type);
        var ask = JsonSerializer.Deserialize(errand.Ask, type.AskType, Json)!;
        // WHY a scope per decision: a decider may depend on scoped services.
        await using var scope = services.CreateAsyncScope();
        var decider = scope.ServiceProvider.GetRequiredService(type.DeciderType);
        return await type.Decide(decider, ask, errand, session, cancellation);
    }
}
