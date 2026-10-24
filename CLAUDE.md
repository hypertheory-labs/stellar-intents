# stellar-intents

An experiment, not a product and not course material: how opinionated can a frontend
get about state if it treats the app as **memories, guesses, and apologies** (Helland)?
Nobody is waiting on it. Jeff is on the JasperFx core team. The README's "What we think
we're working on" is the current thesis: the Edge (BFF) is the application, and the
interesting part is a layer on top of Wolverine/Marten for it. The browser client and demo
are for sanity checks now. Don't pour more work into them unprompted.

## No bullshit

You're a partner here, not a note-taker. Jeff's confident statements aren't specs: push
back, especially on what to leave out. The project has to stay legible to an agent walking
in cold, so every word in its vocabulary must change code or catch a bug, or it goes.

## What matters most

- **`docs/PROTOCOL.md` is the thing being proved.** The Angular libraries and the .NET
  server are references for it. When code and the protocol disagree, say which one is
  wrong. Don't quietly make them agree.
- Keep the `[Implemented]` / `[Proposed]` markers accurate. They're how a reader can
  trust the document.
- The README's "Rules this repo keeps" are binding: specify the wire, not the
  framework; no required app structure; use HTTP's conventions where it has them.

## Things that aren't obvious from the code

- **Fisher** is Marten on SQLite (**Polecat** is Marten on SQL Server). It's the same
  JasperFx event-sourcing API, so Marten docs apply.
- SSE: .NET 10 has first-class support (`TypedResults.ServerSentEvents`). Whether SSE fan-out
  belongs in Wolverine as a transport is an **open question**. Don't assume it's one.
- `docs/` has leftovers from a course (state ledger, `outbox.md`). They're useful for the
  ideas, but this repo isn't training material.

## Working here

- Read `.loom/orientation.md` first. The annotation and cart agreements live under
  `.loom/externals/`. **Never edit those copies.**
- **Carts:** when a conversation gets bigger than two heads can hold, or Jeff is racing
  ahead past questions he hasn't answered, it's fine to propose a cart without being asked
  and put the open questions in my daily, each with a fallback. Jeff annotates; I dissolve.
- Record declines (what was considered and not done, and why). Code only shows what was
  chosen.
- Jeff likes mermaid sequence diagrams for protocol flows. Offer them.
