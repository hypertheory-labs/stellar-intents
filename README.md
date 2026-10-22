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

| Path                                                   | What                                                                                                         | Status                       |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------ | ---------------------------- |
| [`docs/`](docs)                                        | The wire protocol.                                                                                           | Draft 0.1                    |
| [`client/angular/projects/outbox`](client/angular/projects/outbox) | The browser outbox: intents, receipts, candidates, apologies. NgRx Signal Store.               | Phase 1 (in memory), tested  |
| [`client/angular/projects/url-state`](client/angular/projects/url-state) | The URL as a source of truth: `withRouter`, `withQueryParam`, `withMatrixParam` for Signal Store. | Experimental, tested         |
| [`server/dotnet/ReferenceApi`](server/dotnet/ReferenceApi) | A reference server: library holds, on Wolverine + Fisher (event-sourced), .NET 10.                       | Builds; §1–3 with hold-specific event names |
| [`examples/angular-holds`](examples/angular-holds)     | A real page using the outbox, from the course these ideas came out of.                                       | Reference code, not runnable here |

## Running things

**Server** (`http://localhost:5261`, API reference at `/scalar`):

```bash
dotnet run --project server/dotnet/ReferenceApi
```

It keeps its data in a local SQLite file (`app.db`), created and seeded on first run. In
Development, `POST /dev/reset` starts the data over.

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

1. **Generic stream events** on the server: `GET /events` with `intent-decided` and
   `invalidate` (PROTOCOL §3), replacing the hold-specific stream. Heartbeats.
2. **A read cache** (Angular): keyed by read key, memory only (never candidates),
   invalidated by the stream, stale-first with `fetchedAt`. A `cachedHttpResource` and a
   Signal Store `withQuery`.
3. **Promotion without an effect**: intent types say where their real item shows up, and
   the cache retires done intents when it does.
4. **Durability**: intents (and maybe reads) in IndexedDB, resent with the same id after a
   reload.

## Rules this repo keeps

- **Specify the wire, not the framework.** Each piece is useful alone; pieces meet through
  HTTP and events, never by importing each other.
- **No required app structure.** Functions and providers, removable in an afternoon.
- **If HTTP already has a convention, use it.**
