# Errands in insurance

**2026-10-26.** The second of three sample domains, written the same way as
[the library](errands-library.md). Sketches, not code. The business answers are
**invented** to show the shape; they aren't any insurer's. The bind errand is
an earlier quote → bind sketch (`quotes.md`, since deleted) rewritten as an errand.

Errands are addressed as resources of their own here (`PUT /errands/{id}`,
`DELETE /errands/{id}` to withdraw), which is the idea under test from the cart. Reads
stay where they are (`GET /policies/{id}`).

## Ask for full coverage on a vehicle

**Customer, or an agent for them → Insurer:** cover this vehicle for its replacement
value (comprehensive and collision).

| Ends      | When                                                                                                                                 |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| accepted  | It's covered as asked, from a date.                                                                                                  |
| declined  | We won't offer full coverage on it (a salvage title, say). Reason: "We can't offer full coverage on a vehicle with a salvage title." |
| withdrawn | Taken back before it was decided.                                                                                                    |

- **Offers, when declined** (closed): _cover it for liability only_, at a stated
  premium. That's the "best we can do". It's another errand, and choosing it is one
  request.
- **Taking it back:** withdraw, free until decided.
- **After it's accepted, possible:** remove the vehicle; change the coverage.
- **Projections changed:** the policy, its vehicles, the premium.

**This is the test of "accepted on terms".** Is "liability only" an acceptance with
different terms, or a decline with an offer? With three outcomes it has to be the
second, and I think that's right. The rule that falls out:

> **Accepted means they got what they asked for, or better. Anything less is declined,
> with an offer.**

The old sketch's "accepted on terms" (the premium moved under 2%, and the difference was
waived) passes that rule: they got the quoted premium, which is what they asked for.
Liability-only doesn't: it's less, so the person has to choose it. The person always
consents to getting less.

## Bind a quote

**Customer, or an agent for them → Insurer:** bind quote Q.

| Ends      | When                                                                                                                                                                                    |
| --------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| accepted  | Bound at the quoted premium. Also when the premium moved within tolerance (waived).                                                                                                     |
| declined  | The premium moved more; the quote expired and re-rates higher; the vehicle was reported stolen or totalled; the customer already has a policy from another quote. Each with its reason. |
| withdrawn | Taken back before it was decided.                                                                                                                                                       |

- **Offers, when declined** (closed): _bind at the new premium_ (when it moved, or
  expired); _talk to an agent_ (stolen or totalled). Nothing for "already have a policy"
  except _replace it_, if the business wants that offer at all.
- **Taking it back:** withdraw, free until decided.
- **Starts, when it's undecided for an hour:** Insurer → Underwriter, _decide this
  binding_. That errand is the insurer's own, not the customer's. The customer is told
  "an underwriter is looking at it": a convenience message, not a promise.
- **Projections changed:** the policy, the customer's policies, the quote.

**"Bind a revised quote instead" no longer needs `supersedes`.** It's two requests:
`DELETE /errands/{a}` (withdraw the first binding), then `PUT /errands/{b}` (bind Q2). If
the first was decided before the withdraw arrived, the withdraw fails cleanly (it's too
late), and the second is declined with "you already have a policy from Q1", offering
_replace it_. Nothing goes wrong between the two requests, so nothing needs them to be
atomic. That's the first evidence for errands as resources.

## Remove a vehicle

**Customer, or an agent for them → Insurer:** take this vehicle off my policy, from
this date.

This is a forward errand, not an undo: the vehicle really was on the policy, and that
stays true.

| Ends      | When                                                                                         |
| --------- | -------------------------------------------------------------------------------------------- |
| accepted  | It comes off on the date asked.                                                              |
| declined  | A lender requires coverage on it. Reason: "Your lender requires this vehicle to be insured." |
| withdrawn | Taken back before it was decided.                                                            |

- **Offers, when declined** (closed): _talk to an agent_. Not _remove it anyway_.
- **Taking it back:** withdraw, free until decided. After it's accepted but before the
  date, changing your mind is another forward errand, _keep the vehicle_.
- **After it's accepted, possible:** keep the vehicle (until the date).
- **Projections changed:** the policy, its vehicles, the premium, from the date.

**The effective date is the notification strain again.** Accepted means "agreed: it
comes off on the 1st", not "it's off". Just like "tell me when it's ready", the errand's
outcome is the commitment, and the thing it commits to happens later. That's twice in two
domains, so I now think it's the rule, not a strain: **an errand's outcome is a
commitment; what it commits to may come later.**

## What the insurer would report

| Errand                   | Started by          | Withdraw window | Offers (closed)                                   | Starts                                        | Possible after               |
| ------------------------ | ------------------- | --------------- | ------------------------------------------------- | --------------------------------------------- | ---------------------------- |
| ask for full coverage    | customer, or agent  | until decided   | cover for liability only                          | none                                          | remove vehicle, change cover |
| cover for liability only | customer (as offer) | until decided   | none                                              | none                                          | remove vehicle, change cover |
| bind a quote             | customer, or agent  | until decided   | bind at new premium; talk to an agent; replace it | underwriter review, after 1 h (insurer's own) | none in this sketch          |
| remove a vehicle         | customer, or agent  | until decided   | talk to an agent                                  | none                                          | keep the vehicle             |
| decide this binding      | insurer             | none            | none                                              | none                                          | none                         |

What the spec alone says: why a salvage title rules out full coverage, the 2% tolerance,
the one-hour escalation, and why "remove it anyway" isn't offered.

## Found by writing it

- **Accepted means what they asked for, or better.** Anything less is declined with an
  offer, so the person always consents to getting less. Three outcomes held.
- **An errand's outcome is a commitment.** It's the second domain where what's committed
  to happens later (an effective date, after a notification). It's the rule now.
- **`supersedes` isn't needed** if errands are resources: withdraw, then ask again. The
  race resolves itself, because a late withdraw fails cleanly.
- **The guard needs a third source.** "A person expresses intents, some ahead of time as
  preferences" doesn't cover an agent acting for the customer. That's neither the
  person now nor a preference. It's **delegated authority**. So: the Edge starts an
  errand for a person only from an intent they expressed, now, ahead of time, or through
  someone they've authorized.
- **Two kinds of errand started by someone else:** on the person's behalf (from
  preferences, or by an agent), and the business's own (the underwriter review). The
  report should keep them apart.
