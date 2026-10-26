using Edge.Sse;
using Fisher;
using Fisher.Services;

namespace ReferenceApi.Streaming;

/// <summary>
/// Sends "invalidate" (PROTOCOL §3) for the documents a commit wrote, whoever wrote them.
/// The app says once, per document type, which reads a changed document makes stale.
/// </summary>
/// <remarks>
/// WHY a Fisher listener, and not Wolverine middleware or each handler: a read goes stale
/// because a document changed, and an inline projection writes its document during the
/// save, so only the commit knows what changed. A listener on the store hears every
/// commit: a handler, an errand's decider, or code that never goes through Wolverine.
///
/// WHY straight to the hub: this runs after the commit, which is the moment "publish only
/// after commit" asks for. It's not durable, though: if the process dies between the
/// commit and here, that invalidation is lost. Clients recover when their stream drops
/// and reconnects ("subscribed": re-check everything). Missing one is the cost of a
/// convenience message, not a broken promise.
///
/// FOR REAL: deleted documents aren't looked at (nothing here is deleted yet), and async
/// projections would need the daemon's listener instead.
/// </remarks>
public sealed class Invalidations(SseHub hub) : IDocumentSessionListener
{
    private readonly Dictionary<Type, Func<object, IEnumerable<ServerSentEvent>>> _byType = new();

    /// <summary>When a <typeparamref name="T"/> is written, send these.</summary>
    public Invalidations When<T>(Func<T, IEnumerable<ServerSentEvent>> changed)
    {
        _byType[typeof(T)] = document => changed((T)document);
        return this;
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken cancellation)
    {
        foreach (var document in commit.Inserted.Concat(commit.Updated))
            if (_byType.TryGetValue(document.GetType(), out var changed))
                foreach (var sent in changed(document))
                    hub.Publish(sent.Group, sent.EventType, sent.Data);
        return Task.CompletedTask;
    }

    public Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken cancellation) => Task.CompletedTask;
    public void DocumentLoaded(object id, object document) { }
    public void DocumentAddedForStorage(object id, object document) { }
}
