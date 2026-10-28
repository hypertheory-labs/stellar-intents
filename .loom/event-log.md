# Event log

The durable record: decisions, declines, and findings that outlived a cart. Each entry is
tagged with the cart it came from. Everything tagged with one name was considered in a
single sphere of understanding. Each entry says what we believed, not just what we chose,
because only a belief can later be shown wrong.

---

## `quince` (2026-10-22 to 2026-10-23)

The round: from "PROTOCOL.md is the thing being proved", where does the work go next?
It built the generic stream, the read cache, identity, durability's cheap part, and
remedies, and it moved the vocabulary a long way.

### Decisions

- **Commands act for the session's user** (`quince`). `PUT`/`DELETE` need a session
  (401 without one); the body never names the user. Cancelling someone else's hold is a
  403, not a 404, because a 404 says the hold doesn't exist and a client could wait for
  it. We believed the existence leak was small, because ids are random UUIDs.
- **Declared invalidation fires when the server has recorded the intent** (`quince`):
  `received`, `done`, or `needs-attention` after being received. The first wording ("on
  success") was too narrow, because a `202` already changes reads. These are exactly the
  moments the server sends `invalidate`, and that symmetry is what we believed would keep
  the two signals honest.
- **The double fetch stays, for now** (Jeff, `quince`). The server's `invalidate` and the
  intent type's declaration both arrive for one change, so every key is fetched twice.
  It's safe. Jeff's feeling is that it's a symptom of something bigger, so we watch for
  what turns up next to it rather than patching it. (Candidate fix, not taken: ignore
  `from: 'intent'` while the stream is live.)
- **Stay with SSE, not WebSockets** (`quince`, agreed in chat). Heartbeats aren't a
  reason to switch (browser JavaScript can't see ping/pong). What would justify it is
  frequent client-to-server traffic on the live connection, and even that could be a
  plain POST alongside SSE.
- **The read cache is built on `resourceFromSnapshots`** (`quince`). One `resource()` per
  reader would fetch twice for two readers. The cache owns shared entries, and each
  reader gets a real Angular Resource. The API is **experimental** in Angular 22:
  reconsider if it's removed.
- **Apologies are sorted by remedy, not by mechanism** (Jeff and Claude, `quince`).
  Reasons belong to the business and can't be counted; what the person can do next can.
  Built: `try-again`, `wait`, `no`, `sign-in`. We believe the set stays small and that a
  second domain will add one more remedy, not many.
- **A 5xx or 401 should land in a state that isn't final** (Jeff, `quince`). Neither is a
  decision. Today they land in `needs-attention`, so "try again" means a new id, which is
  a duplicate if the 5xx came after commit. Chosen over "a 5xx promises nothing was
  recorded", because a server can't always keep that promise. Not built yet.
- **Half-formed ideas stay out of PROTOCOL.md** (`quince`). They live in the cart and
  reach the protocol only as [Proposed] once decided. Markers can sit on a bullet when it
  differs from its section.
- **Durability starts with the cheap part** (`quince`). Intents are stored per user,
  before sending, and only status-checked after a reload, never resent. We believed the
  honest promise is "this browser remembers", and that the user-owned version needs the
  server (a ledger).

### Declines

- **Duende.BFF** (Jeff, `quince`). It's commercial, and this project will be open source
  if it becomes anything. We'll build a small BFF by hand on YARP. If that gets too
  rough, Jeff will reread the license before we reconsider.
- **Running JasperFx.Events in the browser with WASM** (Jeff, `quince`). Too
  experimental. What survived is "the browser as a kind of node."

### Findings

- **A misnamed Wolverine handler drops messages silently** (`quince`). A class named
  `ProtocolMessageHandlers` (plural) isn't discovered (only `*Handler`/`*Consumer` are),
  so every message went nowhere with no exception. A test caught it. For a frontend
  developer arriving through this onramp, that's a sharp edge. Possibly worth raising with
  JasperFx.
- **SSE over HTTP/1.1 runs out at about six connections per origin** (`quince`). Each
  tab's stream holds one open, so the seventh tab hangs. The profile must require
  HTTP/2. Our dev server is HTTP/1.1.
- **Query-string read keys only match exactly** (`quince`). That pushes URL design toward
  tag-shaped paths (`/patrons/{id}/holds`), as the DCB reading predicted.
- **The hold limit can race** (`quince`). Two evaluations for one patron could both see
  two holds and both place. Marten's DCB (tags, `FetchForWritingByTags`) is the textbook
  fix, if Fisher supports it. Not checked.
- **One channel per connection makes Jeff a little uneasy** (`quince`). Claude's guess:
  the risk is around the channels, not in them. Broadcasts loop over every connection,
  empty per-patron entries are never removed, and the hub is per process. Jeff remembers
  Wolverine primitives that might fit.
- **The edge already knows what each session reads: its GETs** (Jeff, `quince`). It
  could send `invalidate` only to sessions with a covered key. The `subscribed` rule makes
  that self-healing. A design candidate for the edge, which doesn't exist yet.

### Fallbacks that became the decision

- **Shapes A/B/C for whether the server knows about intents.** Unanswered, so the
  fallback held: no code toward any shape, only the shape-neutral parts (stream,
  `invalidate`, session). The question is still open.
- **"The edge"** is the name in prose, with no package name until one earns itself.
- **Hand-written `invalidate` keys stay the baseline** until DCB is checked.
- **The intent type's `invalidates` declaration lives on the client first.** Built.
- **Refetches bypass the browser cache with `cache: 'no-cache'`** (needs `withFetch()`).
  Checked and built.
- **Not done, though the fallback said it would be:** reading Wolverine's HTTP
  transport and inbox code to see whether "the browser as a sender" holds up. Carried
  forward.

---

## `sorrel` (2026-10-23 to 2026-10-26)

The round: switching to "remedy" changed the language, so what's under it, and what do we
build next? It named the final outcomes and fixed a real bug, then grew a vocabulary
faster than code could test it. Midway the plot moved: the Edge is the application, not
the browser. So the round ended by cutting the docs and the vocabulary back to what's
built, and extracting `Edge.Sse`.

### Decisions

- **Outcomes are accepted, declined, or withdrawn, and "withdrawn" has the wide meaning**
  (Jeff, `sorrel`, `0867c8a`). It means taken back before it was decided, by any means,
  including a cancel racing the decision. Jeff called the narrow meaning ("before it
  left the browser") almost meaningless.
- **`interrupted`: a state that isn't final** (`sorrel`, `60543fa`, confirmed by Jeff as
  "way more honest"). It's the two generals' courier: `unknown` can't be designed away,
  only made safe to ask about. This builds quince's "a 5xx or 401 should land in a state
  that isn't final".
- **Fisher until it lacks something we need, then Marten on Postgres, with no
  workarounds** (Jeff, `sorrel`). Fisher stays while it's easier to iterate on and to
  hand someone ("download this and run it").
- **Aspire and the mock OAuth server wait for Postgres** (Jeff, `sorrel`).
- **Receipts have plain library defaults, and the business overrides them** (Jeff,
  `sorrel`). That way neither of us plays the business person while building.
- **The thesis: the Edge is the application, and the UI is one aspect of it** (Jeff,
  `sorrel`, README `ef837a4`). The part worth offering is a layer on top of Wolverine
  and Marten: "we keep the browser informed and track sessions; you plug in what happens
  when." More browser durability is paused. We believed that if the Edge holds pending
  intents, the browser only has to keep one until the Edge has it.
- **`docs/` says only what's built; speculation goes in `unsettled/`** (Jeff, `sorrel`,
  `0611667`, `0881691`). The vocabulary was cut to nine words, each one in code: intent,
  intent id, outcome, receipt, candidate, memory, apology, remedy, read key. We believed
  that a word which changes no code and catches no bug makes the repo illegible to anyone
  arriving cold, and that IRMTO had started to repel scrutiny (Jeff's own diagnosis).
- **No bullshit** (Jeff, `sorrel`, `28fcf71`). Claude is a partner, not a note-taker.
  Jeff's confident statements aren't specs, and pushback matters most on what to leave
  out.
- **SSE is `Edge.Sse`, a package of its own, and not a Wolverine transport** (`sorrel`,
  `4dfc8cf` to `93db94f`). A browser doesn't ack and isn't a peer. Wolverine is in it
  only so that events go out after commit, and that's tested for a handler failure and
  for a commit failure. Connections join groups (Mercure-style topics) instead of a
  single audience. A transport would be the backplane between instances.
- **Build for our needs, not other people's, yet** (Jeff, `sorrel`). The backplane waits
  for the Postgres and Aspire move (`unsettled/backplane.md`).

### Declines

- **WebTransport for broadcasts** (Jeff, `sorrel`). We believed SSE already does one-way
  fan-out over HTTP, with the session cookie and reconnects for free, and nothing here
  needs a stream from the browser to the server.
- **Wolverine.HTTP inside `Edge.Sse`** (Jeff, `sorrel`). Plain ASP.NET Core endpoints keep
  the package small. Jeff's feeling about Wolverine.HTTP endpoints is "not quite enough
  HTTP and too much Wolverine".
- **Building SSE on Wolverine's transport model** (Jeff and Claude, `sorrel`). It fits the
  backplane, not the browser.
- **The browser posting to Wolverine's HTTP transport** (`sorrel`, from a summary of the
  docs). It's the inside door: the browser would name internal message types, with no
  Edge to translate, stamp the actor, or check anything. The same transport from the Edge
  to the domain stays plausible and untested.
- **OpenTelemetry in the browser** (Jeff, `sorrel`). The Edge can start the trace on its
  own: it sees every intent arrive.
- **url-state in this repo** (Jeff, `sorrel`, `cf8d394`). It was never part of this, and
  it lives elsewhere.

### Findings

- **Writing both intents side by side found a real bug** (`sorrel`, fixed in `b10ab10`
  and `2dfe0fa`). A hold cancelled before its decision left the place-hold intent waiting
  forever. Neither intent's happy path showed it; the slot for "what if the world
  differs" asked the question. That's what we believe is worth keeping from IRMTO, even
  with the rest of it in `unsettled/`.
- **Wolverine's handler naming bit a second time** (`sorrel`). Test handlers in a class
  named `TestHandlers` weren't discovered. Quince's finding stands, with two occurrences
  now.
- **Fisher's binaries have DCB** (`sorrel`, not run). `FetchForWritingByTags`,
  `EventTagQuery`, `DcbConcurrencyException`; `Wolverine.Fisher` 6.50.2 has `[DcbModel]`.
  This answers quince's "not checked" as far as reading binaries can. The hold limit's
  race is still unfixed.
- **A receipt can lie** (`sorrel`). Receipt text is per intent type, not per state, so an
  `interrupted` intent still read "We've received your request". Still true in the demo.
- **Automatic retries never give up** (`sorrel`). Every reconnect resends this page's
  interrupted intents, with no budget. Still true, and paused with durability.
- **Every remedy we have is about the transport** (`sorrel`, `unsettled/edge-weight.md`).
  `try-again`, `wait`, `no`, `sign-in`: none is about the library. A domain's remedy is
  an offer, an errand the person can choose next. The outbox squeezes a domain decline
  into the transport remedy `no` (`outbox-store.ts:161`).
- **Today the domain is the clerk, by accident** (`sorrel`). `EvaluateHoldHandler` applies
  the library's rule and emits `IntentDecided` to the browser. Written twice, as a
  counter Edge and a clerk Edge (`unsettled/edge-weight.md`), the counter fails because
  the domain has to import the protocol.
- **"Complete CALM"** (Hellerstein, arXiv 2602.09435, read through a subagent's summary
  only). A spec is coordination-free if it's monotone under an order you declare. A
  cancel racing a decision needs coordination, but only at one authority per intent; the
  hold limit is a named non-monotone pattern, which is why it needs DCB. Jeff wants to
  read it himself. The PDF was a specimen and goes with the cart; the arXiv id finds it.

### Fallbacks that became the decision, or didn't happen

- **The five language cleanups.** Only `docs/outbox.gaps.md` changed, and it was deleted
  in the docs collapse rather than renamed. `apology.actions`, `resolve()` beside
  `settle()`, `examples/angular-holds`, and the `outbox` name are all unchanged. The
  fallbacks said they'd go; nobody did it, and the browser is now paused.
- **Quote → bind** was specified (`6781dc1`, now `unsettled/quotes.md`) but not built.
  **DCB and escalation on `EvaluateHold`** weren't tried. The reorientation to the Edge
  overtook both.
- **The two-owner remedy principle** wasn't written into PROTOCOL. It went to
  `unsettled/protocol-proposed.md` with the collapse.
- **"Why not given / when / then"** wasn't rewritten around Jeff's three points (actor to
  actor, *Rests on*, *Otherwise* beside *Then*).

### Discarded from the write-ahead log

Discarding is an act, so it's listed. Anything here that still matters gets reintroduced
with a fresh reason.

- **The basis family**: a basis per intent, basis checks at the Edge, materiality over
  age, conditions instead of versions, attestations, accommodations at the audience's
  edge, Bobcat for partial objects. These were never tested against code, and the word
  "basis" was retired. What a basis would name is still a README question.
- **Framing**: one inside across browser and Edge; actor → intent → actor; actors as
  roles in a staging; receipts as a chain of custody; intents having an address (the
  person, the session, this browser); one fact, many messengers. They're not wrong, but
  none of them changed code.
- **Mechanisms with no code yet**: escalation as the system's own intent; retry policies
  that give the receipt an honest deadline; a paused listener as "stuck for everyone";
  sending the client's age instead of a timestamp; `supersedes={id}`; the friction ledger.
  Some of these may come back through errands; if so, they'll come back with a reason.

## `larch` (2026-10-26 to 2026-10-27)

The round: the Edge runs **errands**. How big is one, how do they chain, and what do
three domains say is general? Three sample domains (library, insurance, a shop) gave
errands eleven rules (`unsettled/errands.md`). Then the code followed: `Edge.Errands`,
errands as the one way to change anything, invalidations from the commit, `made`, and
offers. PROTOCOL went from 0.4 to 0.6.

### Decisions

- **An errand has one final outcome; anything long is a chain of errands** (Jeff,
  `larch`). It held in all three domains, including an inter-library loan over days. The
  strain is specific: a promise an accepted errand made ("we'll tell you when it
  arrives") needs a message that isn't an outcome. Not built.
- **Withdrawing happens only before a decision; after it, changing things is a new
  errand that can be declined** (Jeff, `larch`). Cancelling a placed hold is
  `cancel-hold`, not a `DELETE`. We believed this is what made the cancel-first bug
  possible: one `DELETE /holds/{id}` did both jobs.
- **The spec is closed world** (Jeff, `larch`): what isn't listed isn't offered. Built as
  the closed list of errand types (an unknown type is a `400`) and the closed list of
  offers per type (checked at startup; offering anything else throws).
- **Errands are the one way; resources are only read** (Jeff, `larch`, `9dfb09f`,
  `0cf4c96`). `PUT`/`GET`/`DELETE /errands/{id}`. Jeff: "let's be all Guido and say 'one
  good way'", and "`/errands` or nothing". He sees mapping intents to endpoints as part
  of why Wolverine.Http fizzled out for him. Reads stay resources, or we'd reinvent
  caching "like SOAP did".
- **Registering a decider isn't app structure** (Jeff, `larch`).
  `AddErrands(e => e.Add<PlaceHold, PlaceHoldDecider>("place-hold", window: …))`, which
  Jeff called beautiful. The README rule "no required app structure" stands.
- **Invalidations come from a Fisher commit listener** (`larch`, `6df07a6`, `46adb63`).
  The app maps a document type to read keys once (`When<Hold>(HoldKeys.Changed)`);
  handlers publish nothing. It sends after commit but outside Wolverine's outbox, so a
  crash can lose one. Jeff: "close enough" fits the spirit, since we're "taking some of
  the guess work out of making guesses".
- **Only the server says which reads are stale** (Jeff, `larch`, `d30cc5a`). Intent
  types' `invalidates` declarations are gone.
- **An accepted errand says what it `made`; a guess is retired when memory has that id**
  (`larch`, `f48d7fe`, `cc6d0f0`). Jeff: invalidations are hints anyway; the id isn't.
- **A `200` is read by its body** (`larch`, `014d805`). A repeat of a declined errand is
  a `200`, and the outbox had read it as accepted. Found while writing PROTOCOL §1.
- **IRMTO is set aside; the specification language comes after the code** (Jeff,
  `larch`, `ed0ddce`). "See where the code leads us - and we are getting close - and
  *then* figure out a 'specification language' around it."
- **Offers: whole errands under one id per group, given by the server** (Jeff's idea,
  `larch`, `8789148`, `905147a`, `2baed1c`). The same id for every offer in a decline
  means the existing rule (same id, another body is a `409`) makes them "choose one" with
  no new code. It made the lookup a load by key and gave `offeredBy` (the "because of"
  link) for free. The wire is a list of `{ id, choose }`, though C# makes one group, so
  choices that don't exclude each other can arrive without a breaking change. Jeff:
  "almost the best hypermedia-as-the-engine-of-application-state thing we've done": the
  client never builds an offer, it sends back what it was given.
- **`swap-hold` is one errand, not cancel then place** (`larch`, `ba76aeb`). We believed
  the two-request version is the "Lego bricks in the least trustworthy place" problem:
  the browser would be the only one to notice a hold given up for nothing.
- **`reason`, not `detail`, on `intent-decided`** (Jeff, `larch`, `b9d854c`). `detail`
  belongs to problem details; a decline isn't a problem.
- **Unions wait** (Jeff, `larch`). `Decision` is a sealed-record hierarchy. C# 15 has union
  types, but Jeremy plans a JasperFx major version for .NET 11 that's mostly unions, so we
  move with that.

### Declines

- **Wolverine middleware for invalidations** (`larch`). It wraps a handler: before the save
  it sees events but not the documents the inline projection writes, and it misses
  writes outside handlers. The commit listener sees both.
- **A "the server restarted, everything is suspect" broadcast** (Jeff's idea, declined
  together, `larch`). A restart drops every stream, and the reconnect's `subscribed`
  already means exactly that.
- **A test for a commit that fails after the listener** (Jeff, `larch`): "if it failed,
  turn off the lights. go home." Maybe later.
- **Retiring a guess on "the next read after the outcome"** (Jeff chose it first,
  `larch`). A probe showed `invalidate` reaches the browser before `intent-decided`, so a
  read could show the real hold beside the guess. Ordering the stream would only make
  that rare, not impossible.
- **Keeping `PUT`/`DELETE /holds/{id}` beside errands**, or keeping PROTOCOL §1 neutral
  between them (Jeff, `larch`).
- **Keeping `invalidates` for the no-stream case** (Jeff, `larch`): "we aren't ready to
  cave in to 'well, if you don't like these peaches, here's another tree you can
  shake'." Without a stream, a guess now stays until its list is read again. If that
  case ever matters, the fix is generic (refetch what's watched when the tab becomes
  visible), not per type.
- **Keeping IRMTO as the named specification idea, or `holds-in-irmto.md` as evidence**
  (`larch`). Its one finding, a cancel overtaking a hold, is now answered by errands. The
  sample domains' form (how it ends, offers, what it starts) is the one that became code.
- **Building offer groups now** (`larch`). The only real case found, two missing items in
  one order, is the "partial stock" strain `unsettled/errands.md` already names.
- **Offer ids made by the client, one per offer** (`larch`). Two tabs could take two swaps.
- **Amending an errand inside its window** (Jeff, `larch`). Rare; it was an example of
  things floating on a time window, not a requirement.

### Findings

- **Fisher applies inline projections before it checks the stream version** (`larch`,
  `8816145`). An `Apply` that throws to guard an invariant beats the version check and
  turns a lost race into a crash. Let the version check hold the rule (`Errand` ignores a
  second outcome); handlers retry `ConcurrencyException` themselves. In CLAUDE.md.
- **A commit's change set lists the projection documents it wrote** (`larch`, probe on
  Fisher 1.20.2). An inline snapshot comes through in `Updated` (it's an upsert), not
  `Inserted`. Async projections would go through `IDaemonChangeListener`: not tried.
- **`invalidate` reaches the browser before `intent-decided`** (`larch`). The listener
  sends at commit; the outcome goes through Wolverine after. In CLAUDE.md and PROTOCOL.
- **What it's for** (Jeff, `larch`): a fair spec-driven process. The library is as much
  framework as is expedient, and no more; the rest is a legible artifact a person and an
  AI work on together, that can answer "wait, why are we doing it this way?" "If it ends
  up like a 2001 XML rules engine, we've failed." The proposal Jeff agreed to: the
  library says what's registered (errands, outcomes, offers, windows), the way ASP.NET
  emits OpenAPI, and the written artifact is *compared* with that, never executed.
  Nothing is built for it. README thesis 4 says the language comes later.
- **Things in the code by accident, and what became of them** (`larch`). The 3-second
  delay became a declared withdraw window. `HoldStatus.Requested`/`Rejected` became the
  errand's lifecycle and left the hold. A cancel landing first became withdrawing,
  settled at one authority. `HoldKeys` stayed, but only the commit listener uses it.

### Fallbacks that became the decision

- **The three domains** were the library, insurance, and a shop: the daily's fallback.
  No answer was recorded before they were written.
- **A domain decline is still the transport remedy `no`** (`outbox-store.ts`). Offers ride
  beside it rather than replacing it. The fallback said nothing would change in the
  browser until errands existed; they exist, and this one didn't change.
- **No withdraw button in the demo.** My lean (the client is for sanity checks), raised
  and never answered. Withdrawing is tested on the server and in PROTOCOL's scenarios.

### Discarded from the write-ahead log

- **Two kinds of telling, the guard, partial stock as a continuation, when a person is
  told through their channels, a person's history of intentions** (`GET /errands`).
  They're rules 3, 8 and 9 and "Still straining" in `unsettled/errands.md`, which keeps
  them until they're built or deleted. The log doesn't need a second copy.
- **SCIP for the artifact's pointers** (Jeff). Not tried; it comes back with the artifact,
  if the artifact comes back.
- **The spec artifact as a graph in a UI** (Jeff: "not the focus now").
- **Jeff's daily item**, Edge.Errands depending on JasperFx.Events instead of Fisher (so
  it could sit on Fisher, Marten, or Polecat). Jeff moved it to his own todo.

## `rowan` (2026-10-28)

The round: Edge.Errands off Fisher, onto JasperFx.Events' interfaces, so it runs on
Marten too (Polecat later). It picked up larch's last item, Jeff's daily item that he had
moved to his own todo. Then the ReferenceApi audit's question: where the protocol glue
goes. Along the way, two logs for the Critter Stack team:
`server/dotnet/store-differences.md` (facts, per store) and
`unsettled/critter-stack-questions.md` (what we couldn't answer ourselves).

### Decisions

- **Edge.Errands uses only JasperFx.Events' interfaces** (`rowan`, `d91e2fe`, `c033ac2`,
  `f33132b`). Handlers ask Wolverine for `IDocumentSessionOperations` and the read/write
  interfaces, which are one outboxed session underneath (seen in generated code).
  `GET /errands/{id}` goes through the bus, because the read-only interface has no
  `FetchLatest`. Why now (my push-back on Jeff's "Fisher is the lowest common
  denominator"): Fisher was only the store we'd tested, not proven smallest, and the
  people the thesis is for are on Marten. The decider API had settled the day before.
- **Deciders get `ErrandSession`: `Events` and `Query`** (`rowan`, `f33132b`; my
  fallback, which Jeff agreed: "agree with it all"). A type of our own, because no
  JasperFx interface has both. Only what the hold deciders use.
- **A package per store: `AddFisher(...)` / `AddMarten(...).IntegrateWithErrands()`**
  (Jeff, `rowan`, `e8edc0b`, `b88375d`). Separate packages are "probably the
  consistent-with-the-rest-of-jasperfx move", though he doesn't like it; the extension
  was his idea, after I found nothing tested needs the `Errand` snapshot. Each store's
  configuration is its own type, so there's nothing shared to hang it on. Jeff doubted it
  later, from reading the other `IntegrateWith...` methods, and dropped the doubt:
  "pretty unfounded". The names are for now: "let's save naming things for another round!"
- **`ErrandOffer` is registered** (`rowan`, `e8edc0b`). Nothing registered it; with
  on-demand tables off, which is how production apps run, every request fails (the
  request handler loads an offer each time). Found by the test written to justify the
  package.
- **Edge.Errands references `JasperFx.Events.SourceGenerator` itself, private, same version
  as JasperFx.Events** (`rowan`, `e8edc0b`, `1db7357`). It used to come bundled with
  Fisher; without it, the build passes and startup throws. Jeremy (via Jeff): "you'll just
  have to add the source generator for projection support", and private plus matching
  versions is "Always safest, yes."
- **One shared store suite** (`rowan`, `66747fd`): eight tests at the bus, no HTTP, no
  holds, run by `Edge.Errands.Fisher.Tests` and `Edge.Errands.Marten.Tests` (Postgres in
  Docker, port 5499). On-demand tables off; `AddResourceSetupOnStartup()` builds the
  registered schema and Wolverine's tables on both stores.
- **The split for the protocol glue** (Jeff: "the split seems to be the right way to go",
  `rowan`, `4c21973`). Edge.Errands owns `intent-decided`'s body
  (`ErrandEnded.ToIntentDecided()`), because its fields are an errand's, and apps copying
  them get them wrong quietly. The app only says who hears it. The wire didn't change.
- **The ReferenceApi ends with `RunJasperFxCommands`** (`rowan`, `6445d75`), so
  `codegen preview` and `codegen write` work.

### Declines

- **A third "Edge" package for the glue** (`rowan`). It would keep the README rule's
  letter, but a package whose only job is "use both" is the framework the first rule
  warns about, and invalidations would need it per store: two more packages for about
  100 lines, mostly the app's own decisions.
- **Leaving all the glue in the app** (`rowan`). Each app would copy `intent-decided`'s
  shape, the part apps get wrong quietly.
- **`Invalidations` in a library, for now** (`rowan`). Implementing JasperFx's
  `IDocumentCommitListener` doesn't make it store-neutral: Marten's listeners don't
  implement it, so it needs an adapter per store, which costs more than the class. Until
  a second app wants it, or the team answers question 7.
- **Three parameters on `Decide` instead of `ErrandSession`** (`rowan`). Every decider's
  signature gets longer for a gap in someone else's interfaces.
- **`LoadAsync` and `Store` on `ErrandSession`** (`rowan`). No decider uses them; add one
  when a decider needs it.
- **Leaving `ErrandOffer` for the app to register** (Jeff's first lean, `rowan`). The
  extension keeps an internal document type out of the app's sight, in one line.
- **A Polecat package this round** (Jeff, `rowan`). No SQL Server here (he "shuddered" at
  running it on this Mac); he has a Windows machine for it later. Every entry in
  `store-differences.md` says "Polecat: not checked".
- **Committing generated handler code, or static mode** (`rowan`). The static-mode run
  was an experiment; its results are in question 2.
- **Fixing the xUnit1051 analyzer warnings** (`rowan`). They predate the round.

### Findings

Most are in `store-differences.md` and `unsettled/critter-stack-questions.md`, which
keep them until they're answered. These are the ones that don't live there, or matter
beyond them.

- **In static mode, a handler a library adds later is silently missing** (`rowan`,
  question 2). Static mode uses the pre-generated registry and skips the assembly scan,
  so 6.30's mismatch check never sees it. What upgrading a package with handlers does to
  an app that doesn't run `codegen write` again. Jeff: "this might make a good docs PR
  eventually."
- **Nothing tested needs the `Errand` snapshot** (`rowan`). With it removed, all tests
  passed: an errand is at most three events. It stays, unchanged. Nothing tests the
  errand `ETag`, so whether `Version` is right without the snapshot is unknown.
- **`Idempotency-Key` now only repeats the URL** (`rowan`, the audit). It equals the id in
  `PUT /errands/{id}`, so it does nothing for idempotency, and nothing checks that the
  two match. Its one remaining job is the CSRF defence. Not discussed; changing it means
  changing PROTOCOL §1.
- **The README's "never by importing each other" predates both server libraries**
  (`rowan`). It's in the first commit (`a02d28a`), when the pieces were the browser's and
  the wire. Jeff: it "probably is from prior to us figuring out the wire stuff". I had
  stretched it to two .NET libraries in one process. Rewording it is open.
- **The race test can't tell what Marten does first** (`rowan`). It passes on Marten, but
  `Errand`'s `Apply` never throws, so it can't tell whether Marten runs inline
  projections before the version check, as Fisher does (question 8). A test-only aggregate
  that throws would; it tests the store, not errands, so it wasn't built.

### Fallbacks that became the decision, or didn't happen

- None became the decision silently: Jeff answered the daily ("agree with it all").
- **Polecat's fallback said `unsettled/` would say "should work, untested".** I didn't
  write that note: `store-differences.md` already says "Polecat: not checked" on every
  entry. Told Jeff; no answer.

### Discarded from the write-ahead log

- **Fisher's `IChangeSet` doesn't implement `IDocumentChangeSet`**, **one outboxed session
  for every interface**, **three ways Wolverine's tables weren't made on Marten.** Each
  is in `store-differences.md` or question 3 now.
- **`ErrandSession` without `LoadAsync`/`Store`.** Promoted, as a decline.
- **Jeff's doubt about `IntegrateWithErrands()`.** He dropped it; recorded under Decisions.
