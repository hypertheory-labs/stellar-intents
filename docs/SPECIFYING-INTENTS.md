# Specifying an intent

**Draft, 2026-10-23.** A way to write down what an intent needs, so a business can answer
it and a test can run it. Not part of the [protocol](PROTOCOL.md) yet. Tried on the
reference domain in [`intents/holds.md`](intents/holds.md), where it found a bug.

## The whole idea in three sentences

1. An intent is a request made on a **basis**: who's asking, what they relied on, and
   until when.
2. Before it becomes manifest, the basis is checked: at the edge as far as it can be,
   and in the domain where it must be.
3. Where the check fails and no rule settles it, the person gets an **apology** with a
   **remedy**.

## The form

# Intent · Rests on · Manifests when · Then · Otherwise

"IRMTO", pronounced "ouch! my toe!"

| Heading            | Says                                                                                                                               |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| **Intent**         | **From → To:** what was asked, in the asker's words. Intents are directed at someone.                                              |
| **Rests on**       | The basis: who's asking, **acting for whom**, and the facts they relied on. Facts, not screens or documents.                       |
| **Manifests when** | The conditions that must still hold when it's decided, and how much change is tolerable.                                           |
| **Then**           | What's recorded (only what the intent owns, never the state around it), and **Tell:** who learns of it, at which address.          |
| **Otherwise**      | For each condition that fails, and **each intent that could get there first**: accept, accept on terms, a remedy, or a new intent. |

### Actors and direction

Every intent goes **from** an actor **to** an actor. The person isn't the only actor: the
business is one too, and so are its staff and its systems.

- **Patron → Library:** place a hold. The usual direction.
- **Library → Librarian:** fulfil this hold. Escalation, caused by the patron's intent.
  It doesn't replace that intent, which stays as the record that they asked.
- **Library → Patron:** still want it? Look again? Shall we call you? An apology that
  offers a choice is an intent addressed to the person, and their answer is their own
  intent coming back.

The client may deliver what the person asked for, as many times as it takes, while it
still means what they meant. It may never ask for something new **on their behalf**.
The business asking something **on its own behalf** (escalating, offering) is a different
actor speaking, which is fine.

**A reading test:** read every arrow out loud. If one ends at "the system", nobody has
decided which actor it is.

### Addresses: who learns, and how durably

**Tell** names an address, and the address implies the delivery promise:

| Address          | Lives           | Delivered                         | For example                  |
| ---------------- | --------------- | --------------------------------- | ---------------------------- |
| **the person**   | durably         | any device, whenever they're back | "Your hold went through."    |
| **the session**  | while signed in | the app, in this session          | "Your sign-in expired."      |
| **this browser** | until it's gone | now, or never                     | "What's on screen is stale." |

A message addressed to the person and delivered only to whatever tab is open is lost when
none is.

## Why not given / when / then

"Given a balance of 100, when I deposit 50, then the balance is 150" treats what the
actor sees as the truth. Another deposit landing at the same time makes the spec a
fantasy. An intent never owned the balance, so its spec can't promise one.

> - **Intent:** Customer → Bank: deposit $50 to account A.
> - **Rests on:** the customer's session (acting for themselves); they may act on A; A is
>   open.
> - **Manifests when:** A is still open when it's decided.
> - **Then:** a $50 deposit is recorded. **Tell:** the customer (the person).
> - **Otherwise:**
>   - A was closed → `no`: "This account was closed on {date}."
>   - The session ended → `sign-in`, then the same intent again.

## A longer one

The answers here are **invented**, to show the shape.

> - **Intent:** Policyholder → Insurer: add vehicle {VIN} to policy {number}.
> - **Rests on:** the session (the policyholder, or an agent acting for them); they're a
>   named insured on the policy; the policy is active; the vehicle isn't reported
>   totalled or stolen; the premium they were quoted.
> - **Manifests when:** the policy is still active; the vehicle still isn't reported
>   totalled or stolen; the premium hasn't risen by more than 2% or $25; the quote is
>   under 30 days old.
> - **Then:** the vehicle is added to the policy at the quoted premium. **Tell:** the
>   policyholder (the person), and the agent if one acted for them.
> - **Otherwise:**
>   - The premium rose by more (customer app) → `look-again`. **Insurer →
>     Policyholder:** here's the new premium; add it at that?
>   - The premium rose by under 10% and they're a VIP (agent app) → accept on terms:
>     honour the quoted premium, recording which edge allowed it.
>   - Totalled or stolen → `no`: "We can't add this vehicle: {reason}." **Insurer →
>     Policyholder:** talk to an agent?
>   - The quote is over 30 days old → `still-want-it`, with a fresh quote.
>   - Undecided after an hour → **Insurer → Underwriter:** decide this addition (caused by
>     this intent). **Tell:** the policyholder: "An underwriter is looking at it."
>   - **The policyholder removes the vehicle first** (another intent, same policy) →
>     withdrawn. **Tell:** this browser, quietly.

## Questions it asks a business

1. **Who is asking whom?** And when someone acts for someone else, on whose authority?
2. **What did they rely on when they asked?** The facts.
3. **Which of those changing would change your answer, and by how much?** Asking about
   materiality works where asking about age doesn't: "how old may it be?" gets "one
   second" or "one year."
4. **For how long may they rely on each fact?** Only where time is part of the promise.
5. **When a material change has happened, what do we do?** Accept, accept on terms, or a
   remedy.
6. **What do we say, and what do we offer?** In the customer's words, with at least one
   choice, even if it's only "I've seen this."
7. **Who needs to know, and how durably?** The person, the session, or this browser.
8. **What else could reach the same actor, about the same thing, before this is
   decided?** The other intents on the same resource, from the same person or anyone
   else. This is the question that found the bug in the holds domain.
9. **If nobody decides in time, who should?** Escalation is the business's own intent.

And across all intents: **which must be exact?** Those get a transaction. Saying
"everything" out loud is what makes it a choice.
