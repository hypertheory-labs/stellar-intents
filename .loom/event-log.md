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
