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
