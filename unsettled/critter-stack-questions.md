# Questions for the Critter Stack team

What we couldn't answer ourselves while making Edge.Errands a library that runs on
Marten, Fisher, and (later) Polecat. Each question says what we saw, what we did about
it for now, and what we'd like to know. A question leaves this file when someone
answers it: into the code, into [`store-differences.md`](../server/dotnet/store-differences.md),
or deleted.

`store-differences.md` is the facts, checked per store. This file is what we don't know.

Versions: Fisher 1.20.2, Marten 9.50.2, JasperFx.Events 2.87.1, WolverineFx 6.50.2.

## Code generation: two different things

These two are easy to mix up, and both bit us.

- **JasperFx.Events' source generator** runs at **compile time**, in the assembly that
  holds an aggregate. It writes the code that calls the aggregate's `Apply` and `Create`
  methods (for us, `Errand`). There's no runtime fallback: if it didn't run, startup
  throws `InvalidProjectionException`.
- **Wolverine's handler code generation** runs at **startup** (dynamic mode, needing
  `WolverineFx.RuntimeCompilation`, i.e. Roslyn), or ahead of time (`codegen write`,
  then static mode). It writes the code around each handler: opening the outboxed
  session, passing it in, saving. Edge.Errands' handlers go through this like an app's.

### 1. How should a library with an aggregate get the source generator?

**What we saw.** Edge.Errands used to reference Fisher, and Fisher's package bundles the
generator, so it ran without anyone asking. When Edge.Errands dropped Fisher, the build
still succeeded, and startup failed with `InvalidProjectionException` ("no
source-generated dispatcher found ... Errand"). JasperFx.Events' `buildTransitive`
targets file explains that store packages bundle it (Marten too) and dedupes copies.

**What we did.** Edge.Errands references `JasperFx.Events.SourceGenerator` 2.87.1 itself,
with `PrivateAssets="all"`, so apps don't get a second copy from us.

**What we'd like to know.**
- Is a direct reference to the standalone generator the intended way for a store-neutral
  library?
- Is `PrivateAssets="all"` right? The exception's own text warns that it hides the
  analyzer from downstream projects. We think that's fine (an app's aggregates get it
  from their store package), but we're guessing.
- Must the generator's version track JasperFx.Events exactly? We matched them by hand.
- A silent build and a startup failure is the worst moment to find out. Could the build
  warn when an assembly has aggregate-shaped types and no generator?

### 2. What should a library do about Wolverine's runtime compilation?

**What we saw.** Our test projects need `WolverineFx.RuntimeCompilation`, or Wolverine
refuses to start in dynamic mode. Edge.Errands itself doesn't reference it, and we
think that's right: dynamic or static is the app's choice.

**What the docs say** ([migration](https://wolverinefx.io/guide/migration.html#runtime-code-generation-moved-to-the-wolverinefx-runtimecompilation-package-breaking),
[codegen](https://wolverinefx.io/guide/codegen.html), read 2026-10-28).
- Roslyn left core `WolverineFx` in 6.0, so static and Native AOT apps can ship without
  it. The package auto-registers when referenced. Without it, dynamic or auto mode fails
  at startup with the message our test projects hit.
- Both pages put the requirement on *applications*. Nothing says a library should or
  shouldn't reference it. We read that as "the host decides", but it's our reading.
- `codegen write` writes into the *entry* project, which compiles the generated code.
  For handlers in a class library, the codegen page says to use
  `Discovery.IncludeAssembly` and leave `ApplicationAssembly` alone, which is what
  `UseErrands()` does.
- Since 6.30, a missing pre-built handler fails host start in static mode, rather than
  losing messages.
- `codegen write` needs `return await app.RunJasperFxCommands(args);` as the last line
  of `Program.cs`. Our ReferenceApi ends with `app.Run()`, which is why
  `dotnet run -- codegen preview` started the server instead of previewing the code.

**What we tried** (2026-10-28, Fisher; the ReferenceApi now ends with
`RunJasperFxCommands`; the static-mode changes and generated files weren't kept).
- `codegen write` in the ReferenceApi wrote the library handlers (Edge.Errands' four,
  Edge.Sse's one) into `ReferenceApi/Internal/Generated/WolverineHandlers/`, beside the
  app's own. The generated code names the store's `OutboxedSessionFactory`, which is fine
  there: the app knows its store, so the library stays neutral.
- Static mode with `WolverineFx.RuntimeCompilation` removed: started, and request, read,
  the scheduled decision, and withdraw all worked through HTTP, without Roslyn.
- Deleting one generated handler file breaks the app's build (the generated registry
  names it), so that mistake can't ship.
- **A handler the library adds later is silently missing.** We added a handler to
  Edge.Errands, rebuilt without running `codegen write` again, and started in static
  mode. It started without complaint: Wolverine logged "Using pre-generated Wolverine
  HandlerRegistry (6 handler types); skipping assembly scan", so it never saw the new
  handler, and the 6.30 mismatch check can't catch what it never discovers. That's what
  upgrading a library package would do to an app that doesn't regenerate. (We didn't send
  the missing handler a message, so what happens then is untested.)

**What we'd like to know.**
- Is "a library never references `WolverineFx.RuntimeCompilation`" right?
- Is the silently-missing library handler known? Could static mode compare the
  pre-generated registry with the handlers in `Discovery.IncludeAssembly` assemblies,
  or should the docs tell apps to run `codegen write` again whenever a package with
  handlers changes? (This might make a docs PR.)
- Our handlers ask for JasperFx's interfaces (`IDocumentSessionOperations`,
  `IDocumentReadOperations`, `IDocumentWriteOperations`) instead of a store's session.
  On Fisher, the generated code opens one outboxed session and casts it to each. Is that
  supported on purpose, on every store, or does it just happen to work?

## Schema and AutoCreate

### 3. Wolverine's tables under Marten with `AutoCreate.None`

**What we saw.** With Marten's `AutoCreateSchemaObjects = AutoCreate.None`, Wolverine
doesn't create its own tables as it starts, and startup fails on `wolverine_nodes`
(read early, in `loadAgentRestrictionsAsync`). None of these created them:
- `IDocumentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync()` before start
- `IWolverineRuntime.Storage.Admin.MigrateAsync()` before start
- `WolverineOptions.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate`

Only `services.AddResourceSetupOnStartup()` did. On Fisher, Wolverine creates its tables
as it starts regardless of `AutoCreate.None`.

**What we did.** The shared store tests use `AddResourceSetupOnStartup()`.

**What we'd like to know.** Should any of the three have worked? If so, it's a bug and
not a difference. If not, what's the intended setup for "no tables created on demand"
with Wolverine on Marten? And which store's behaviour is the intended one?

### 4. Fisher's `ApplyAllDatabaseChangesOnStartup()` with `AutoCreate.None`

**What we saw.** On Fisher, `AutoCreate.None` plus `ApplyAllDatabaseChangesOnStartup()`
creates nothing, not even the event tables. An explicit
`ApplyAllConfiguredChangesToDatabaseAsync()` does. (Not tried on Marten.)

**What we'd like to know.** Intended? It reads like "apply all changes on startup", and
it applies none.

## The shared interfaces

### 5. Is there meant to be one store-neutral session for libraries?

**What we saw.** No JasperFx.Events interface has events, loading, and storing together:
`IDocumentSessionOperations` (events, save), `IDocumentReadOperations` (load, query),
`IDocumentWriteOperations` (store). And a query session's `Events` is
`IQueryEventStore`, which has no `FetchLatest`.

**What we did.** Handlers ask Wolverine for each interface they need. Deciders get our
own `ErrandSession`, which wraps the two they use (`Events` and `Query`). Our
`GET /errands/{id}` reads through a writing session, to get `FetchLatest`.

**What we'd like to know.** Is a combined interface planned, or is asking for each the
intended way? Should `IQueryEventStore` have `FetchLatest`?

### 6. A store-neutral way to configure a store?

**What we saw.** `AddFisher` and `AddMarten` return different types with nothing shared,
so `IntegrateWithErrands()` is a package per store (`Edge.Errands.Fisher`,
`Edge.Errands.Marten`), as `IntegrateWithWolverine()` is. All it does is register a
snapshot projection and one document type, through calls that are spelled the same on
both stores.

**What we'd like to know.** Is there, or will there be, a JasperFx-level hook for "add
these projections and document types to whichever store this is"? With one, a library
like this would need no per-store packages at all.

## Behaviour

### 7. Inline projections before the version check

**What we saw.** On Fisher, inline projections run before the stream version check, so
an `Apply` that throws to guard a rule turns a lost race into a crash instead of a
`ConcurrencyException`. We built around it (`Errand`'s `Apply` ignores a second outcome
instead of throwing). Our race test passes on Marten too, but since our `Apply` never
throws, it can't tell whether Marten does the same.

**What we'd like to know.** Is this order intended on Fisher? Does Marten do the same?
Should an `Apply` ever throw to guard an invariant?
