# Outbox wire protocol

**Draft 0.1, 2026-10-22.** How a browser and an API agree about commands the user asked
for, and about reads that have gone stale. Framework-neutral: this page is the contract;
the Angular `outbox` library and the Wolverine/Fisher backend in this repo are one
implementation of each side.

If you're building a client for this protocol (in any framework), read
[The client](#the-client) and [Conformance scenarios](#conformance-scenarios). If you're
building a server, read [The server](#the-server). Everyone reads [Vocabulary](#vocabulary).

Each section is marked:

- **[Implemented]**: the reference backend and Angular app do this today.
- **[Proposed]**: designed, not built yet. Expect it to change.

---

## Vocabulary

| Term             | Means                                                                                                                  |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **Intent**       | Something the user asked the server to do, with a client-generated id. A request, not a fact.                          |
| **Intent id**    | A UUID the client makes (`crypto.randomUUID()`). It is the idempotency key **and** the id of the resource it creates.  |
| **Outcome**      | What finally happened: `done` (good) or `needs-attention` (bad). **An outcome is final.**                              |
| **Memory**       | The server's data, as the client last fetched it. The client never edits it to "look right."                           |
| **Candidate**    | The client's guess at what an intent will become. Kept apart from memory and dropped when memory has the real item.    |
| **Read key**     | The path (and query) of a GET, relative to the API root, e.g. `/holds?patronId=p1`. Identifies a cached read.          |

The client-side states an intent moves through (`requested`, `sent`, `unknown`,
`received`, `done`, `needs-attention`, `failed-to-send`) are the client's business; the
wire only carries what the server knows. They're defined in the outbox
[README](../client/angular/projects/outbox/README.md#lifecycle).

---

## 0. Identity [Implemented]

Who the user is never travels in a URL or a payload the client composes. The reference
server does all of this, with a Development-only cookie sign-in standing in for a real
identity provider.

- **The browser sends the credentials on its own:** a session cookie, `HttpOnly`,
  `SameSite=Strict`. (`EventSource` can't send an `Authorization` header anyway.) Who
  issues the cookie (an edge that keeps the user's tokens server-side, usually) is outside
  this protocol.
- **Unauthenticated means `401`, never a redirect.** A `fetch` or an `EventSource` can't
  follow a login page.
- **There is a session resource** the client can ask "am I signed in?" (the reference uses
  `GET /session`, always `200`, with the answer in the body). `EventSource` can't tell a
  `401` from a server that went away, so when the stream drops, a client asks this
  before reconnecting, and stops reconnecting if it's signed out.
- **Unsafe requests without an `Idempotency-Key` are rejected** (`400`, problem+json).
  §1 already requires the header. Enforcing it also makes it a CSRF defence: a custom
  header can't cross origins without a preflight. With `SameSite=Strict`, that's all of
  it, with no antiforgery token. (Reasoned, not yet reviewed by a security person.)
- **[Proposed]** The server **MAY** close the stream at any time, including when the
  user's access token expires. The reconnect goes back through whatever refreshes the
  session, or comes back `401`.

---

## 1. Commands [Implemented]

### Request

A command is an HTTP `PUT`, `POST`, `PATCH`, or `DELETE`, sent with the intent id as its
idempotency key:

```http
PUT /holds/0b6e0c3e-6a5c-4c4e-9f6e-2f0d8f1c7a11 HTTP/1.1
Idempotency-Key: 0b6e0c3e-6a5c-4c4e-9f6e-2f0d8f1c7a11
Content-Type: application/json

{ "patronId": "p1", "titleId": 42, "branchCode": "MAIN" }
```

- The client **MUST** send `Idempotency-Key: {intent id}` on every command.
- When a command creates something, **prefer `PUT` to the resource's own URL with the
  intent id as its id.** Then "has the server's version arrived?" is an exact check: memory
  contains an item with that id. (`POST` with an `Idempotency-Key` is allowed when the
  server must choose the URL, but the client loses that exact check.)
- The client **MAY** send the same command again with the same id at any time (after a
  timeout, after a reload). That must always be safe.

### Response

| Status                        | Means                                                                       | Client moves the intent to                      |
| ----------------------------- | --------------------------------------------------------------------------- | ----------------------------------------------- |
| `202 Accepted`                | Recorded. The outcome comes later.                                          | `received`                                      |
| any other `2xx`               | Done, and the outcome is good.                                              | `done`                                          |
| `4xx` / `5xx`                 | Rejected. Body is `application/problem+json`.                               | `needs-attention` (with the problem's `detail`) |
| no response (status 0)        | As far as the client can tell, it never reached the server.                 | `failed-to-send`                                |
| no answer within the timeout  | **Not a failure.** It may have landed. The request is not cancelled.        | `unknown`, until a late answer or an outcome    |

A `202` **SHOULD** carry `Location:` pointing at the status resource (usually the
resource itself) and **SHOULD** have the resource's current representation as its body.

### Repeats

The server **MUST** treat a repeat of the same id like this:

| The server already has…                      | It answers                                                  |
| -------------------------------------------- | ----------------------------------------------------------- |
| the same id and the same body, still pending | `202` with the current representation                       |
| the same id and the same body, decided       | `200` with the current representation                       |
| the same id with a **different** body        | `409 Conflict`, problem+json                                |

A `DELETE` of something already deleted is still a success (`204`).

### Problem details

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) `application/problem+json`.

- **`detail` is shown to the user.** It ends up in the client's apology ("We couldn't
  place your hold: You already have 3 holds, the limit."). The server **MUST** write it for
  a person and **MUST NOT** put internals (stack traces, SQL, ids the user never saw) in it.
- A client **MUST** cope with a non-problem error body (a proxy's HTML page, an empty
  body): `detail` is then absent, and the client's apology uses its own words.

---

## 2. Status checks [Implemented]

For an intent stuck in `received` or `unknown`, the client can always ask:

```http
GET /holds/0b6e0c3e-6a5c-4c4e-9f6e-2f0d8f1c7a11 HTTP/1.1
```

- `200` with the representation. Whether that representation means `done` or
  `needs-attention` is resource-specific (a hold's `status` is `placed` or `rejected`), so
  the client's intent type supplies the mapping.
- `404` while the command hasn't been recorded yet (it may still be in flight, or it never
  arrived). Not an outcome; the client keeps waiting or resends.

The representation **SHOULD** carry its version as an `ETag`.

The client asks at least: when the user asks ("Check status"), and when the page becomes
visible again. **Status checks are the floor.** The event stream below is an
enhancement: a client that never opens it must still reach every outcome.

---

## 3. The event stream [Implemented]

One [Server-Sent Events](https://html.spec.whatwg.org/multipage/server-sent-events.html)
stream per user, carrying outcomes and invalidations as they happen.

```http
GET /events HTTP/1.1
Accept: text/event-stream
```

The user is whoever the session says (§0). Without one, the stream is `401`.

### Events

#### `subscribed`

```
event: subscribed
data: {}
```

Sent first on **every** connection, including reconnects. It means: *you're live now, and
anything that happened while you were away is not coming down this pipe.* On receiving it,
the client **MUST**:

1. run a status check for every intent in `received` or `unknown`, and
2. refetch every read it is currently showing (or treat them all as stale).

That rule replaces replay. It is always correct and far simpler than `Last-Event-ID`.

#### `intent-decided`

```
event: intent-decided
data: {"intentId":"0b6e0c3e-…","outcome":"needs-attention","detail":"You already have 3 holds, the limit."}
```

| Field      | Type                              | Notes                                                       |
| ---------- | --------------------------------- | ----------------------------------------------------------- |
| `intentId` | string (UUID)                     | The command's `Idempotency-Key`.                            |
| `outcome`  | `"done"` \| `"needs-attention"`   |                                                             |
| `detail`   | string, optional                  | User-presentable, same rules as a problem's `detail`.       |

The client resolves the intent. If the intent is unknown to it (another tab, already
retired) or already decided, the client ignores the event.

#### `invalidate`

```
event: invalidate
data: {"keys":["/holds","/titles/42"]}
```

Reads under these keys have changed on the server. The client marks matching cached reads
stale and refetches the ones on screen.

- **Keys are paths relative to the API root, starting with `/`.** A key matches a read key
  when it's equal to the read's path, or a prefix of it ending at a segment boundary:
  `/holds` matches `/holds`, `/holds?patronId=p1`, and `/holds/0b6e…`, but **not**
  `/holdsArchive`.
- **A prefix can't select a query parameter.** `/holds?patronId=p1` matches only a read
  asked with exactly that query (same parameters, same order). So the URL design decides
  how precisely a server can invalidate: `/patrons/p1/holds` can be matched by prefix,
  while `/holds?patronId=p1` can only be matched exactly.
- The server **MAY** send invalidations caused by **other** users (a title's wait list
  changed because someone else placed a hold). This is how reads stay live without
  WebSockets.
- Over-invalidating is safe (an extra refetch). Under-invalidating is the bug.

### Stream rules

- **No replay, no event ids.** An `id:` would promise a resume the server can't keep.
  The `subscribed` rule covers the gap.
- **Publish only after commit.** The server **MUST NOT** send an event about a change
  that isn't durable yet. (In Wolverine: return the integration message as a cascading
  message from the handler, so it's sent only after the transaction commits.)
- **Fan out per connection.** Two tabs for the same user each get every event.
- **Bounded.** A connection that stops reading **MAY** be closed rather than buffered
  forever. The client reconnects and the `subscribed` rule recovers what was missed.
- **Heartbeats.** About every 15–30 s, the server **SHOULD** send a comment line
  (`: keep-alive`) or an event named `heartbeat`, so proxies don't close an idle stream.
  Clients ignore both. (.NET's `SseItem` can't write a comment, so the reference sends
  the event.) It **SHOULD** also disable response buffering and compression for the
  route, and anything proxying the stream must not buffer it.
- **Auth is the session** (§0), never the URL.

---

## 4. Reads [Proposed]

Plain `GET`s. The protocol adds only:

- A read's **read key** is its path and query relative to the API root.
- Representations **SHOULD** carry an `ETag` (the version).
- The server **MAY** send `Cache-Control` (e.g. `max-age=30, stale-while-revalidate=300`)
  for reads that tolerate it. The browser's own cache then serves stale-first for free.
- Collections **MUST** include the ids of items created by commands, so the client can
  see a candidate's real version arrive (and retire the intent).

---

## The client

What a conforming client has, in any framework. Names are suggestions.

1. **Intent store.** Holds intents (plain, serializable data: id, type name, payload,
   timestamps, state). Exposes receipts, candidates, and apologies derived from them.
2. **Intent types, registered in code.** For each kind of command: how to build the
   request from the intent, the receipt text, an optional candidate, an apology, a
   timeout, and how to read an outcome from a status-check response.
3. **Sender.** Sends a command per §1 and maps the response to a state. Never cancels on
   timeout. Applies states with one rule: **once `done` or `needs-attention`, never
   change again** (answers arrive in any order).
4. **Status checker.** §2, on demand and on page-visible.
5. **Stream listener.** Opens §3's stream, reconnects with backoff (browsers sometimes
   leave an `EventSource` `CLOSED` for good, e.g. after an API restart, so don't rely on
   its own retry), runs the `subscribed` rule, resolves on `intent-decided`, and
   invalidates on `invalidate`.
6. **Read cache (optional).** Keyed by read key. Holds memory only, **never candidates**.
   Exposes each read's value, `fetchedAt`, and whether it's stale.
7. **Merge for display.** What the screen shows is memory plus the candidates whose id
   isn't in memory yet, candidates first, with candidates visibly marked as guesses. When
   a `done` intent's id appears in memory, retire the intent.

A client **MUST NOT** write candidates into memory (or the read cache). Because it never
does, it never has to roll anything back.

---

## The server

1. Commands are idempotent on the intent id, per §1's repeat table.
2. Accepted-but-pending answers `202` with `Location`; decided answers `2xx`.
3. Errors are problem+json with a user-safe `detail`.
4. Every command's resource can be fetched for a status check (§2), with an `ETag`.
5. One SSE stream per user (§3): `subscribed` first, events only after commit, fan-out
   per connection, bounded, heartbeats, session auth (§0).
6. When a write changes what a read would return, send `invalidate` with keys that cover
   it, including to other affected users.

With more than one server instance, events need a backplane (a broker every instance
listens to, Postgres `LISTEN/NOTIFY`, Redis pub/sub). The stream's shape doesn't change.

---

## Conformance scenarios

Each is a test a client (or a server, where marked) should pass. Written so a fresh
implementation can be checked without reading the reference code.

**Commands**

1. Enqueue → the request carries `Idempotency-Key` equal to the intent id; the intent is
   `sent`.
2. `202` → `received`. `201`/`200`/`204` → `done`.
3. `409` with problem+json `detail: "X"` → `needs-attention`, and the apology can say "X".
4. `500` with an HTML body → `needs-attention`, no `detail`, no crash.
5. Network failure (status 0) → `failed-to-send`.
6. No answer within the timeout → `unknown`; then a late `202` → `received`; a late `200`
   → `done`.
7. *(server)* Same id, same body, twice → same answer both times, one resource. Same id,
   different body → `409`.

**Outcomes are final**

8. `intent-decided: done` arrives, then the command's late `202` → still `done`.
9. `intent-decided: needs-attention`, then a status check says `done` → still
   `needs-attention`.
10. `intent-decided` for an id the client doesn't hold → ignored, no error.

**Stream**

11. On `subscribed`, every `received`/`unknown` intent gets a status check, and on-screen
    reads refetch.
12. The stream closes → the client reconnects (with backoff) → `subscribed` again → step 11
    again.
13. With the stream never opened, a `received` intent still reaches its outcome through a
    status check.
14. `invalidate {keys:["/holds"]}` → reads `/holds?patronId=p1` and `/holds/abc` go stale;
    `/holdsArchive` does not.

**Display**

15. A candidate shows, marked as a guess, until memory contains its id; then only the real
    item shows (never both, never neither).
16. A `needs-attention` or `failed-to-send` intent shows no candidate, and shows its apology.
17. A `done` intent whose id is in memory is retired.

---

## Not in this protocol (yet)

- **Durability across reloads.** Clients may store intents (IndexedDB) and resend them
  with the same id; §1's repeat rules make that safe. How they store them is their
  business.
- **Ordering between intents.** Every intent is independent. If two must apply in order,
  that's a single command, or a per-type queue in the client.
- **Replay.** Deliberately left out; see the `subscribed` rule.
- **Retry after a decision.** A retry of a decided intent is a **new** intent with a new
  id. Reusing an id is only for an outcome that was never known.

## Reference implementations

- **Client (Angular, NgRx Signal Store):** [`client/angular/projects/outbox`](../client/angular/projects/outbox),
  with a page using it in [`examples/angular-holds`](../examples/angular-holds).
- **Server (Wolverine + Fisher, the Marten API on SQLite, .NET 10):** [`server/dotnet/ReferenceApi/Holds`](../server/dotnet/ReferenceApi/Holds):
  `HoldEndpoints.cs` (§1, §2) and `HoldKeys.cs` (which reads a change makes stale); the
  stream itself, which knows nothing about holds, is in [`Streaming/`](../server/dotnet/ReferenceApi/Streaming)
  (§3), and identity in [`Session/`](../server/dotnet/ReferenceApi/Session) (§0).
  Conformance tests: [`ReferenceApi.Tests`](../server/dotnet/ReferenceApi.Tests).
