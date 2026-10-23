# Specifying an intent

**Draft, 2026-10-23.** A way to write down what an intent needs, so a business can answer
it and a test can run it. Not part of the [protocol](PROTOCOL.md) yet.

## The whole idea in three sentences

1. An intent is a request made on a **basis**: who's asking, what they relied on, and
   until when.
2. Before it becomes manifest, the basis is checked: at the edge as far as it can be,
   and in the domain where it must be.
3. Where the check fails and no rule settles it, the person gets an **apology** with a
   **remedy**.

## The form

# Intent · Rests on · Manifests when · Then · Otherwise

| Heading            | Says                                                                                         |
| ------------------ | -------------------------------------------------------------------------------------------- |
| **Intent**         | What the person asked for, in their words.                                                   |
| **Rests on**       | The basis: who's asking, and the facts they relied on. Facts, not screens or documents.      |
| **Manifests when** | The conditions that must still hold when it's decided, and how much change is tolerable.     |
| **Then**           | What's recorded. Only what the intent owns, never the state around it.                       |
| **Otherwise**      | For each condition that fails: accept anyway, accept on terms, or a remedy, and what we say. |

## Why not given / when / then

"Given a balance of 100, when I deposit 50, then the balance is 150" treats what the
actor sees as the truth. Another deposit landing at the same time makes the spec a
fantasy. An intent never owned the balance, so its spec can't promise one.

> - **Intent:** deposit $50 to account A.
> - **Rests on:** the person may act on A (their session); A is open.
> - **Manifests when:** A is still open when it's decided.
> - **Then:** a $50 deposit is recorded.
> - **Otherwise:** A was closed → `no`: "This account was closed on {date}." The session
>   ended → `sign-in`.

## A longer one

The answers here are **invented**, to show the shape.

> - **Intent:** add vehicle {VIN} to policy {number}.
> - **Rests on:** the person is a named insured on the policy; the policy is active; the
>   vehicle isn't reported totalled or stolen; the premium they were quoted.
> - **Manifests when:** the policy is still active; the vehicle still isn't reported
>   totalled or stolen; the premium hasn't risen by more than 2% or $25; the quote is
>   under 30 days old.
> - **Then:** the vehicle is added to the policy at the quoted premium.
> - **Otherwise:**
>   - The premium rose by more (customer app) → `look-again`: show the new premium and
>     let them decide again.
>   - The premium rose by under 10% and they're a VIP (agent app) → accept on terms: honour
>     the quoted premium, recording which edge allowed it.
>   - Totalled or stolen → `no`: "We can't add this vehicle: {reason}." Offer: talk to an
>     agent.
>   - The quote is over 30 days old → `still-want-it`, with a fresh quote.

## Questions it asks a business

1. **What did they rely on when they asked?** The facts.
2. **Which of those changing would change your answer, and by how much?** Asking about
   materiality works where asking about age doesn't: "how old may it be?" gets "one
   second" or "one year."
3. **For how long may they rely on each fact?** Only where time is part of the promise.
4. **When a material change has happened, what do we do?** Accept, accept on terms, or a
   remedy.
5. **What do we say, and what do we offer?** In the customer's words, with at least one
   choice, even if it's only "I've seen this."
6. **If the decision comes later, how do they hear about it?**

And across all intents: **which must be exact?** Those get a transaction. Saying
"everything" out loud is what makes it a choice.
