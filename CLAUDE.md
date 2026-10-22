# stellar-intents

An experiment, not a product and not course material: how opinionated can a frontend
get about state if it treats the app as **memories, guesses, and apologies** (Helland)?
Nobody is waiting on it. Jeff is on the JasperFx core team, so pieces may later become
NuGet/npm packages or PRs to Wolverine/Marten. Don't design toward that yet.

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
- `client/angular/projects/url-state/with-router.ts` is meant to go away eventually, in
  favour of the mix-in style of `withQueryParam`/`withMatrixParam`. Not now.
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
