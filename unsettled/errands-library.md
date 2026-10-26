# Errands in the library

**2026-10-26.** The first of three sample domains (the library, insurance, a shop), each
written the same way so they can be compared. Sketches, not code. An **errand** is what
the Edge agrees to take on for a person, knowing the answer may be no. Each one here
ends exactly once: accepted, declined, or withdrawn.

Each errand says what the person asks, how it ends, what it offers when the answer is
no, how it's taken back, what becomes possible after, what it starts, and which
projections it changes. The last section lists what the library would report about
them, which is what a spec would be compared against.

## Place a hold

**Patron → Library:** put a hold on this title, for pickup at this branch.

| Ends      | When                                                                    |
| --------- | ----------------------------------------------------------------------- |
| accepted  | The hold is placed.                                                     |
| declined  | The patron already has three holds. Reason: "You already have 3 holds." |
| withdrawn | The patron took it back before it was decided.                          |

- **Offers, when declined** (closed: nothing else is offered): _swap one of your holds
  for this one_, once per active hold. Each offer is a [swap](#swap-a-hold) errand.
- **Taking it back:** withdraw, free until it's decided. The window today is the
  3-second delay before the decision. Declared, it would ride on the receipt:
  "withdrawable until 10:42".
- **After it's accepted, possible:** [cancel the hold](#cancel-a-hold).
- **Starts, when accepted:** [tell me when it's ready](#tell-me-when-its-ready), for the
  same patron, by their preferred channel.
- **Projections changed:** the hold, the patron's holds, the title's wait list.

## Cancel a hold

**Patron → Library:** I don't want this hold any more.

This is the car-off-the-policy case, inside the library. Once a hold is placed, taking
it away isn't withdrawing anything: the hold existed, and cancelling it is a new errand
moving forward. **Today `DELETE /holds/{id}` does both jobs**: it withdraws a hold that's
still being decided, and cancels one that was placed. That's two errands sharing one
URL, and it's why the cancel-first bug was possible.

| Ends     | When                                                                                                        |
| -------- | ----------------------------------------------------------------------------------------------------------- |
| accepted | The hold is cancelled.                                                                                      |
| declined | The copy is already on the shelf for them. Reason: "It's waiting for you at Main; it'll go back on {date}." |

- **Offers, when declined:** none. (Closed: we don't offer to "un-ready" a book.)
- **Taking it back:** no window; it's decided at once.
- **Starts, when accepted:** withdraws any [tell me when it's ready](#tell-me-when-its-ready)
  for this hold. The library does this, not the patron.
- **Projections changed:** the hold, the patron's holds, the title's wait list.

The decline is invented: the reference has no "on the shelf" state. It's there because
a forward errand that can never be declined probably isn't an errand.

## Swap a hold

**Patron → Library:** give up this hold, and place that one instead, together.

| Ends      | When                                                                                       |
| --------- | ------------------------------------------------------------------------------------------ |
| accepted  | One hold is cancelled and the other placed, in one transaction.                            |
| declined  | The hold to give up is no longer active, or the limit is still reached. Reason says which. |
| withdrawn | Taken back before it was decided.                                                          |

- **Offers, when declined:** none.
- **Starts, when accepted:** the same as place a hold.
- **Projections changed:** both holds, the patron's holds, both titles' wait lists.

The domain needs a `Swap` operation for this. It's people-shaped: librarians do swaps at
the desk.

## Tell me when it's ready

**Library → Patron, started by the library on the patron's behalf:** when a copy is on
the shelf for you, we'll tell you by text, email, or phone.

**Where the chain strains.** This is the first errand nobody asked for in the moment;
it comes from the patron's standing preferences. And its one outcome isn't the
interesting moment:

| Ends      | When                                                          |
| --------- | ------------------------------------------------------------- |
| accepted  | The library has agreed to tell them. That's almost immediate. |
| declined  | No channel to reach them (no preferences). Reason says so.    |
| withdrawn | The hold was cancelled first.                                 |

The book arriving is _not_ an outcome. It happens long after the errand was accepted.
So either:

- **the errand is the commitment** ("we'll tell you"), and the arrival is a _message to
  the person_ that the accepted errand promised, or
- **the errand stays pending until the book arrives**, and "accepted" means "we told
  you". Then a pending errand can last weeks.

I lean to the first. An errand's outcome is what was agreed, and what the agreement
leads to is a message. But it means the library also needs a way to send a person a
message that isn't an outcome, delivered whenever they're next around. That's the
durable "late receipt" the old carts kept parking. **One outcome per errand survives,
but it leaves a gap: messages that keep a promise.**

## Ask another library for a loan

**Patron → Library:** we don't own it; borrow it from another library for me.

The slow-channel case. The decision comes from another library, days later.

| Ends      | When                                                                           |
| --------- | ------------------------------------------------------------------------------ |
| accepted  | A lending library agreed to send a copy.                                       |
| declined  | No library would lend it. Reason: "No library in the network can lend it now." |
| withdrawn | Taken back before a lender agreed.                                             |

- **Offers, when declined:** _ask us to buy it_. That's another errand, out of scope here.
- **Taking it back:** withdraw, free until a lender agrees. The library tells the lender
  "never mind" if it had asked. That's internal: the patron doesn't see it.
- **After it's accepted, possible:** cancel the loan, which can be declined once the copy
  has shipped ("It's on its way; you can return it unread").
- **Starts, when accepted:** tell me when it's ready.
- **Projections changed:** the patron's requests.

**Pending for days is fine.** The receipt says something true while it waits ("We've
asked other libraries. They usually answer within three days."), and the person's
session keeps the errand. "The other library said yes" turned out to be a real
ending, because something becomes possible at that point (cancel the loan, and the
notification), so the chain held. The answer to "is accepted the end?" is yes, as long
as what follows is a message or another errand.

## What the library would report

This is what a spec gets compared with. All of it comes from registrations; none of it
is prose.

| Errand                  | Started by          | Withdraw window       | Offers (closed)  | Starts when accepted    | Possible after |
| ----------------------- | ------------------- | --------------------- | ---------------- | ----------------------- | -------------- |
| place a hold            | patron              | until decided         | swap a hold      | tell me when it's ready | cancel a hold  |
| cancel a hold           | patron              | none                  | none             | (withdraws the notice)  | none           |
| swap a hold             | patron (as offer)   | until decided         | none             | tell me when it's ready | cancel a hold  |
| tell me when it's ready | library, for patron | none                  | none             | none                    | none           |
| ask another library     | patron              | until a lender agrees | ask us to buy it | tell me when it's ready | cancel a loan  |

What the spec alone says, and code can't: why three holds; why we don't offer to
un-ready a book; what the receipts say.

## Found by writing it

- **Withdraw and cancel are two errands that today share `DELETE`.** Withdrawing applies
  only before a decision; after it, taking something away is a forward errand that can be
  declined. The insurance car case and this are the same shape.
- **One outcome per errand held**, including over days. It strained once, on
  notifications, and the strain is specific: we need messages that keep a promise an
  accepted errand made.
- **Errands started by the library on a person's behalf** exist (tell me when it's
  ready), and they're fine as long as the person's preferences are what started them.
- **Every errand changes a hold or a request, and the projections follow from that.**
  Nothing here needed to name a URL.
