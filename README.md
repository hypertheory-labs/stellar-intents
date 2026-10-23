[#](#) stellar-intents

Conventions, and small reference implementations, for web apps that are honest about time.

When a user clicks "Place hold", nothing has happened yet: they've **asked**. This repo
treats that request as an **intent**: a message with its own id, sent idempotently,
walked through a lifecycle the screen can always describe truthfully. Guesses about what
it will become are kept apart from the server's data, and when the guess doesn't hold, the
user gets an apology that was written in advance.

Almost nothing here is invented. It's HTTP (`Idempotency-Key`, `202 Accepted`, problem
details, `ETag`) and Server-Sent Events, with conventions written down so any client and
any server can meet in the middle.

## Start here

**[docs/PROTOCOL.md](docs/PROTOCOL.md)** is the contract: commands, status checks, the
event stream, reads, and conformance scenarios. Everything else in this repo is one
implementation of one side of it.

## What's here

| Path                                                                     | What                                                                                              | Status                            |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------- | --------------------------------- |
| [`docs/`](docs)                                                          | The wire protocol.                                                                                | Draft 0.1                         |
| [`client/angular/projects/outbox`](client/angular/projects/outbox)       | The browser client: intents, receipts, candidates, apologies; the event stream; a read cache.     | In memory, tested                 |
| [`client/angular/projects/url-state`](client/angular/projects/url-state) | The URL as a source of truth: `withRouter`, `withQueryParam`, `withMatrixParam` for Signal Store. | Experimental, tested              |
| [`server/dotnet/ReferenceApi`](server/dotnet/ReferenceApi)               | A reference server: library holds, on Wolverine + Fisher (event-sourced), .NET 10.                | §0–3, tested                      |
| [`examples/angular-holds`](examples/angular-holds)                       | A real page using the outbox, from the course these ideas came out of.                            | Reference code, not runnable here |

## Running things

**Server** (`http://localhost:5261`, API reference at `/scalar`):

```bash
dotnet run --project server/dotnet/ReferenceApi
```

It keeps its data in a local SQLite file (`app.db`), created and seeded on first run. In
Development, `POST /dev/reset` starts the data over, and `POST /dev/sign-in?patronId=P0042`
gives you a session (commands and the event stream need one):

```bash
curl -c jar -X POST "http://localhost:5261/dev/sign-in?patronId=P0042"
curl -N -b jar http://localhost:5261/events
```

Tests: `dotnet test server/dotnet`.

**Angular libraries:**

```bash
cd client/angular
npm install
npm test        # both libraries, once
npm run build   # both libraries, to dist/
```

## Building a client for another framework

Hand [docs/PROTOCOL.md](docs/PROTOCOL.md) to whoever (or whatever) is building it. The
**Conformance scenarios** section is the acceptance test list, and **The client** section
is the parts list. The Angular `outbox` is one way to do it, not the shape you have to
copy: what has to match is the wire.

## Where this is going

Rough order, smallest useful step first:

1. ~~**Generic stream events** on the server~~: done. `GET /events` with `intent-decided`,
   `invalidate`, and heartbeats, behind a cookie session (PROTOCOL §0, §3).
2. ~~**A read cache** (Angular)~~: done, as `cachedHttpResource` in the outbox library.
   Keyed by read key, memory only, invalidated by the stream or by an intent type's
   declaration, stale-first with `fetchedAt`, and fresh only while the stream is live.
   (A Signal Store `withQuery` isn't built.)
3. **Promotion without an effect**: intent types say where their real item shows up, and
   the cache retires done intents when it does.
4. **Durability**: started. Intents are stored per user in IndexedDB before they're sent,
   and status-checked after a reload (`provideIntentStorage`). Not yet: resending, a time
   limit on old requests, one tab at a time, clearing on sign-out. What storing may
   promise is in PROTOCOL's Durability section.

## Rules this repo keeps

- **Specify the wire, not the framework.** Each piece is useful alone; pieces meet through
  HTTP and events, never by importing each other.
- **No required app structure.** Functions and providers, removable in an afternoon.
- **If HTTP already has a convention, use it.**
