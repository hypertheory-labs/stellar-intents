# Where the stores differ

Marten (Postgres), Fisher (SQLite), and Polecat (SQL Server) share the JasperFx event
sourcing API, but not always its behaviour. This is what we've run into while making
Edge.Errands depend on JasperFx.Events instead of Fisher. Each entry says which stores
it was checked on. "Not checked" means we don't know, not that it's the same.

Versions: Fisher 1.20.2, Marten 9.50.2, JasperFx.Events 2.87.1, WolverineFx 6.50.2.
The shared tests are `Edge.Errands.StoreTests`, run by `Edge.Errands.Fisher.Tests` and
`Edge.Errands.Marten.Tests`.

## Behaviour

**Inline projections run before the stream version check.** An `Apply` that throws to
guard a rule beats the version check, so a lost race becomes a crash instead of a
`ConcurrencyException`. We let the version check hold the rule and retry
`ConcurrencyException` (`Edge.Errands/Errand.cs`).
Fisher: yes. Marten: not checked (the race test passes on both, but `Errand`'s `Apply`
never throws, so it can't tell). Polecat: not checked.

## Schema

**With `AutoCreate.None`, Fisher's `ApplyAllDatabaseChangesOnStartup()` creates
nothing,** not even the event tables. With `CreateOnly` or `CreateOrUpdate` it creates
what's registered. An explicit `ApplyAllConfiguredChangesToDatabaseAsync()` does create
it.
Fisher: yes. Marten: not checked. Polecat: not checked.

**With `AutoCreate.None`, Wolverine's own tables follow Marten's setting and not
Fisher's.** On Fisher, Wolverine creates its tables as it starts anyway. On Marten it
doesn't, and startup fails on `wolverine_nodes`. Marten's explicit
`Storage.ApplyAllConfiguredChangesToDatabaseAsync()` doesn't create them, and nor does
Wolverine's `Storage.Admin.MigrateAsync()`, nor `AutoBuildMessageStorageOnStartup =
CreateOrUpdate`. `AddResourceSetupOnStartup()` creates both the store's registered
tables and Wolverine's, on both stores, which is what the shared tests use.
Fisher: Wolverine creates its tables. Marten: it doesn't. Polecat: not checked.

**Where the explicit schema apply lives.** Fisher: `IDocumentStore.ApplyAllConfiguredChangesToDatabaseAsync()`.
Marten: `IDocumentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync()`.
Both: compiled and run. Polecat: not checked.

**A projection's document needs no `Schema.For<T>()`.** `Projections.Snapshot<Errand>()`
alone gets the errand its table. A document written directly (`ErrandOffer`) does need
registering; without it, every shared test fails on both stores.
Fisher: yes. Marten: yes. Polecat: not checked.

## Packaging

**The aggregate source generator comes with the store package.** JasperFx.Events
dispatches an aggregate's `Apply` methods through code a source generator writes into
the aggregate's own assembly. Fisher bundles the generator, so a library that drops its
Fisher reference stops running it, and nothing says so until startup throws
`InvalidProjectionException`. Edge.Errands references `JasperFx.Events.SourceGenerator`
directly now. JasperFx.Events' own build file says Marten bundles it too.
Fisher: yes. Marten: by that comment, not checked. Polecat: not checked.

**Each store's configuration is its own type.** `AddFisher` returns
`FisherConfigurationExpression`, `AddMarten` returns
`MartenServiceCollectionExtensions.MartenConfigurationExpression`, with no shared
interface, so `IntegrateWithErrands()` is a package per store (as
`IntegrateWithWolverine()` is).
Fisher, Marten: yes. Polecat: not checked.

## The shared interfaces

**No one interface has events, loading, and storing.** `IDocumentSessionOperations` has
`Events` (`IEventStoreOperations`) and `SaveChangesAsync`. `LoadAsync` and `Query` are on
`IDocumentReadOperations`, and `Store` is on `IDocumentWriteOperations`. A Wolverine handler
can ask for several; it gets the same session as each, opened by Wolverine's outbox.
Fisher: yes (seen in the generated code). Marten: yes (by the shared tests: a decider's
writes commit with the outcome; the generated code wasn't read). Polecat: not checked.

**A read-only session can't `FetchLatest` through them.** A query session's `Events` is
`IQueryEventStore`, which has no `FetchLatest`. Only the writing session's
`IEventStoreOperations` does.
In JasperFx.Events itself, so all three.
