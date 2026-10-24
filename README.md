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
   of that application. **How much the Edge should know**, whether it's the clerk or only
   the counter, is the open question.
3. **Something on top of Wolverine and Marten.** The wire is specified on its own
   ([the protocol](docs/PROTOCOL.md)), so any client and server can meet. But the part
   worth offering is probably a layer for the Edge: *we keep the person's browser
   informed, track sessions and pending intents, and deliver receipts and apologies; you
   plug in what happens when.* Its shape is unknown.
4. **A way to specify intents.** [IRMTO](docs/SPECIFYING-INTENTS.md) (Intent, Rests on,
   Manifests when, Then, Otherwise) is a way to write down how to be that clerk, when
   twenty years of ACID trained us to be the other guy. Maybe one day it's executable,
   for people and AIs to iterate on.

Almost nothing on the wire is invented. It's HTTP (`Idempotency-Key`, `202 Accepted`,
problem details, `ETag`) and Server-Sent Events, with conventions written down.

## What's here, and how far along

| Path                                                                     | What                                                                                                    | Status                                    |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| [`docs/PROTOCOL.md`](docs/PROTOCOL.md)                                   | The intent wire protocol: commands, status checks, the event stream, reads, conformance scenarios.      | Draft 0.2                                 |
| [`docs/SPECIFYING-INTENTS.md`](docs/SPECIFYING-INTENTS.md)               | IRMTO: how to specify an intent.                                                                        | Draft                                     |
| [`docs/intents/`](docs/intents)                                          | Intents specified that way: library holds (built), and quote → bind (specified, not built).             | Holds built; quotes spec only             |
| [`server/dotnet/ReferenceApi`](server/dotnet/ReferenceApi)               | A reference server: library holds on Wolverine + Fisher (event-sourced, SQLite), .NET 10.               | Protocol §0–3, tested. **No Edge yet.**   |
| [`client/angular/projects/outbox`](client/angular/projects/outbox)       | The browser's half: intents, receipts, candidates, apologies; the event stream; a read cache.           | Tested; intents stored in IndexedDB       |
| [`client/angular/projects/demo`](client/angular/projects/demo)           | A small holds page, for sanity checks against the server.                                               | Runnable                                  |
| [`examples/angular-holds`](examples/angular-holds)                       | A page from the course these ideas came out of.                                                         | Reference code, not runnable here         |

The browser client got built first because there was a half-finished outbox to start
from. The thinking has since moved to the Edge, and the code hasn't followed yet.

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

- **The weight of the Edge.** Does it know what an intent means (the clerk), or does it
  only check the basis and pass the command on (the counter)? Remedies, offers and
  apologies get written in whichever place the answer picks.
- **Quote → bind**, the second domain, [specified before building](docs/intents/quotes.md).
  It's the first one where the basis (a price) can change underneath an intent, and the
  first test of `supersedes`.
- **What a basis names.** An ETag on a whole aggregate locks too much. A validator per
  fact (`/customers/{id}/shipping-address`, perhaps a hash, perhaps checked with `HEAD`)
  may be the wire's version of what DCB tags do on the server.

**Paused**: more browser durability (resending stored intents, one tab at a time, clearing
on sign-out). If the Edge holds pending intents, the browser only has to keep an intent
until the Edge has it, and most of that work stops mattering.

## Rules this repo keeps

- **Specify the wire, not the framework.** Each piece is useful alone; pieces meet through
  HTTP and events, never by importing each other.
- **No required app structure.** Functions and providers, removable in an afternoon.
- **If HTTP already has a convention, use it.**
