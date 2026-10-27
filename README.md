# stellar-intents

An experiment in treating a web app's browser half as what it is: **a node in a
distributed system**, working in its own "now".

We've learned a lot about integrating servers with each other: messages, outboxes,
retries, sagas. A single-page app still mostly behaves as an RPC client, making synchronous
HTTP calls and hoping. The browser works from **memories** (what it last read), makes
**guesses** (what the screen shows before anything is decided), and sometimes owes the
person an **apology** (Helland's words, from distributed systems). This repo takes that
framing seriously at the edge, where it's had the least attention.

## What we think we're working on

This is a guess about where we are, not a settled design.

1. **The person asks; they don't command.** A UI is for expressing intentions, chosen from
   the options it offers. A command is binary: it completes, or it punches you in the
   nose. An intent has a continuation. It's received, it's decided (accepted, declined,
   or withdrawn), and when the answer is no, it comes with what can be done next. Think
   of a good front-desk clerk: when the back office says no, they don't relay the no.
   They say "may I call another location?"
2. **The Edge is the application.** The Edge is the backend-for-frontend: same-origin, it
   keeps tokens out of the browser, and it's the one place that's inside, on the
   person's behalf. Server-rendered apps had an inside. The SPA dream of general APIs
   "assembled like Lego bricks" in the browser moved the orchestration into the least
   trustworthy place there is. We think it belongs at the Edge, and the UI is one aspect
   of that application. **The Edge is the clerk, not the counter**: it runs _errands_ for
   the person (what it agrees to take on, knowing the answer may be no), and the domain
   stays free of the protocol ([why](unsettled/edge-weight.md)).
3. **Something on top of Wolverine and Marten.** The wire is specified on its own
   ([the protocol](docs/PROTOCOL.md)), so any client and server can meet. But the part
   worth offering is probably a layer for the Edge: _we keep the person's browser
   informed, track sessions and pending intents, and deliver receipts and apologies; you
   plug in what happens when._ Two pieces of it exist: `Edge.Errands` (an app writes one
   decider per errand) and `Edge.Sse`.
4. **A way to specify errands, later.** Twenty years of ACID trained us to be the other
   guy, so how to be the clerk is worth writing down: one day executable, for people and
   AIs to iterate on. We tried a form for it before the code and set it aside. The code is
   close to showing what's worth saying, and the language should come from that.

Almost nothing on the wire is invented. It's HTTP (`Idempotency-Key`, `202 Accepted`,
problem details, `ETag`) and Server-Sent Events, with conventions written down.

## What's here, and how far along

| Path                                                               | What                                                                                                                                                                                                                        | Status                                |
| ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- |
| [`docs/PROTOCOL.md`](docs/PROTOCOL.md)                             | The intent wire protocol: errands, status checks, the event stream, reads, conformance scenarios. Only what's built.                                                                                                        | Draft 0.6                             |
| [`unsettled/`](unsettled)                                          | Ideas not built yet: errands' shape and three sample domains, the protocol's proposed parts, a backplane. Not decisions.                                                                                                    | Speculative                           |
| [`server/dotnet/Edge.Sse`](server/dotnet/Edge.Sse)                 | Server-Sent Events for the Edge: groups of connections, sent only after commit (Wolverine), heartbeats, an opening event, ends streams on shutdown. Knows nothing about intents.                                            | Extracted from the reference, tested  |
| [`server/dotnet/Edge.Errands`](server/dotnet/Edge.Errands)         | Errands at the Edge: `PUT`/`GET`/`DELETE /errands/{id}`, a decision after a declared withdraw window, one outcome, a reason on every decline, closed offers ([the shape](unsettled/errands.md)). Knows nothing about holds. | Place, cancel, and swap holds; tested |
| [`server/dotnet/ReferenceApi`](server/dotnet/ReferenceApi)         | A reference server: library holds on Wolverine + Fisher (event-sourced, SQLite), .NET 10. Holds change only through errands; `/holds` is reads.                                                                             | Protocol §0–4, tested                 |
| [`client/angular/projects/outbox`](client/angular/projects/outbox) | The browser's half: intents, receipts, candidates, apologies; the event stream; a read cache.                                                                                                                               | Tested; intents stored in IndexedDB   |
| [`client/angular/projects/demo`](client/angular/projects/demo)     | A small holds page, for sanity checks against the server.                                                                                                                                                                   | Runnable                              |
| [`examples/angular-holds`](examples/angular-holds)                 | A page from the course these ideas came out of.                                                                                                                                                                             | Reference code, not runnable here     |

The browser client got built first because there was a half-finished outbox to start
from. The thinking has since moved to the Edge, and so has the work: the client is for
sanity checks now.

## Running things

**Server** (`http://localhost:5261`, API reference at `/scalar`):

```bash
dotnet run --project server/dotnet/ReferenceApi
```

It keeps its data in a local SQLite file (`app.db`), created and seeded on first run. In
Development, `POST /dev/reset` starts the data over, and `POST /dev/sign-in?patronId=P0042`
gives you a session (errands and the event stream need one):

```bash
curl -c jar -X POST "http://localhost:5261/dev/sign-in?patronId=P0042"
curl -N -b jar http://localhost:5261/events

# in another terminal: place a hold (decided 3 seconds later, on the stream above)
ID=$(uuidgen)
curl -b jar -X PUT http://localhost:5261/errands/$ID -H "Idempotency-Key: $ID" \
  -H "Content-Type: application/json" -d '{"type":"place-hold","titleId":1001,"branchCode":"CEN"}'
```

Tests: `dotnet test server/dotnet`.

**Angular** (the outbox library, and the demo, which proxies `/api` to the server above):

```bash
cd client/angular
npm install
npm test           # the outbox library, once
npm run build      # the outbox library, to dist/
npx ng serve demo  # the sanity-check page
```

## Where this is going

Not a roadmap, just the next questions:

Three sample domains (the library, insurance, a shop) gave errands their shape:
[eleven rules](unsettled/errands.md). Built so far: one outcome, a reason on every
decline, a declared window for withdrawing, cancelling as a forward errand,
invalidations from what a commit wrote, and **offers**: a decline that says what can be
chosen instead ("give up Kindred instead"), each offer a whole errand the browser sends
back as it came, one per id. Next, from the rules not built:

- **What starts what.** An accepted hold starting _tell me when it's ready_, and the
  "because of" link between them.
- **Promised messages.** Telling a person something durably, through their channels, when
  the stream alone can't keep the promise.

**Paused**: more browser durability (resending stored intents, one tab at a time, clearing
on sign-out). The Edge keeps every errand, so the browser only has to keep an intent until
the Edge has it, and most of that work stops mattering.

## Rules this repo keeps

- **Specify the wire, not the framework.** Each piece is useful alone; pieces meet through
  HTTP and events, never by importing each other.
- **No required app structure.** Functions and providers, removable in an afternoon.
- **If HTTP already has a convention, use it.**
