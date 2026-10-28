# Where the stores differ

Marten (Postgres), Fisher (SQLite), and Polecat (SQL Server) share the JasperFx event
sourcing API, but not always its behaviour. This is what we've run into while making
Edge.Errands depend on JasperFx.Events instead of Fisher. Each entry says which stores
it was checked on. "Not checked" means we don't know, not that it's the same.

Versions: Fisher 1.20.2, JasperFx.Events 2.87.1, WolverineFx 6.50.2.

## Behaviour

**Inline projections run before the stream version check.** An `Apply` that throws to
guard a rule beats the version check, so a lost race becomes a crash instead of a
`ConcurrencyException`. We let the version check hold the rule and retry
`ConcurrencyException` (`Edge.Errands/Errand.cs`).
Fisher: yes. Marten: not checked. Polecat: not checked.

**With `AutoCreate.None`, `ApplyAllDatabaseChangesOnStartup()` creates nothing,** not
even the event tables (only Wolverine's own tables appear). With `CreateOnly` or
`CreateOrUpdate` it creates what's registered. With `None`, calling
`ApplyAllConfiguredChangesToDatabaseAsync()` yourself does create it, which is what
`Edge.Errands.Fisher.Tests` does.
Fisher: yes. Marten: not checked. Polecat: not checked.

**Where the explicit schema apply lives.** Fisher: `IDocumentStore.ApplyAllConfiguredChangesToDatabaseAsync()`.
Marten, as we remember its API: `store.Storage.ApplyAllConfiguredChangesToDatabaseAsync()`.
Not run against Marten here.

**A projection's document needs no `Schema.For<T>()`.** `Projections.Snapshot<Errand>()`
alone gets the errand its table from an explicit apply. A document written directly
(`ErrandOffer`) does need registering.
Fisher: yes. Marten: not checked. Polecat: not checked.

## Packaging

**The aggregate source generator comes with the store package.** JasperFx.Events
dispatches an aggregate's `Apply` methods through code a source generator writes into
the aggregate's own assembly. Fisher bundles the generator, so a library that drops its
Fisher reference stops running it, and nothing says so until startup throws
`InvalidProjectionException`. Edge.Errands references `JasperFx.Events.SourceGenerator`
directly now. JasperFx.Events' own build file says Marten bundles it too.
Fisher: yes. Marten: by that comment, not checked. Polecat: not checked.

## The shared interfaces

**No one interface has events, loading, and storing.** `IDocumentSessionOperations` has
`Events` (`IEventStoreOperations`) and `SaveChangesAsync`. `LoadAsync` and `Query` are on
`IDocumentReadOperations`, and `Store` is on `IDocumentWriteOperations`. A Wolverine handler
can ask for several; it gets the same session as each, opened by Wolverine's outbox (seen
in the generated code).
Fisher: yes. Marten: not checked. Polecat: not checked.

**A read-only session can't `FetchLatest` through them.** A query session's `Events` is
`IQueryEventStore`, which has no `FetchLatest`. Only the writing session's
`IEventStoreOperations` does.
Seen in JasperFx.Events itself, so likely all three.
