# Errands: the shape

**2026-10-26.** Not built. This is what three sample domains, written the same way,
have in common: [the library](errands-library.md), [insurance](errands-insurance.md), and
[a shop](errands-shop.md). What's the same in all three is a candidate for a library at
the Edge. What differs is the app's. Read this first, and the domains for evidence.

**An errand** is what the Edge agrees to take on for a person, knowing the answer may be
no. The person has an intent; the Edge runs the errand. (The word is on trial: it stays
only if a type ends up named after it.)

## The rules

Each rule held in all three domains. The links point to where it was tested hardest.

1. **An errand ends exactly once: accepted, declined, or withdrawn.** It held even over
   days ([an inter-library loan](errands-library.md#ask-another-library-for-a-loan)).
2. **Accepted means they got what they asked for, or better. Anything less is declined,
   with an offer.** The person always consents to getting less
   ([full coverage, or liability only](errands-insurance.md#ask-for-full-coverage-on-a-vehicle)).
3. **The outcome is a commitment. What it commits to may come later.** A book arrives
   weeks after "we'll tell you"; a vehicle comes off on the 1st
   ([removing a vehicle](errands-insurance.md#remove-a-vehicle)).
4. **Offers are closed.** An errand lists what it may offer, and nothing else is
   offered. Every offer is another errand, chosen with one request.
5. **Withdrawing happens only before a decision**, and it's free. An errand may declare a
   window that delays its decision on purpose, and the receipt says until when
   ([the shop's 30 minutes](errands-shop.md#place-an-order)).
6. **After a decision, changing things is a new errand, which can be declined.**
   Cancelling a placed hold, removing a vehicle, and cancelling an order are all
   forward errands, not undo ([cancel a hold](errands-library.md#cancel-a-hold)).
7. **A thing accepts errands according to its state**, as a closed table: an order takes
   _cancel_ until it ships, and _start a return_ after
   ([the order's table](errands-shop.md#change-the-delivery-address)).
8. **The Edge starts an errand for a person only from an intent they expressed**: now,
   ahead of time (a preference), or through someone they authorized (an agent). The
   business's own errands (an underwriter review) are the business's, and are reported
   apart ([the guard](errands-insurance.md#found-by-writing-it)).
9. **Two kinds of telling.** _Convenience_ goes over the stream, and losing it costs
   nothing, because the app re-checks when the person comes back. _Promised_ goes through
   the person's channels as well; **the stream alone can't keep a promise.** The
   person's history of errands is the floor under both.
10. **Two kinds of apology.** _Before an outcome_, the transport failed (`try-again`,
    `sign-in`): the library's, the same in every app. _After an outcome_, the business
    broke a commitment: the app's, delivered as a promise, with closed offers and a
    default ([a damaged mug](errands-shop.md#when-the-shop-cant-keep-its-promise)).
11. **An errand says which projections it changes, never URLs.** The Edge maps
    projections to read keys. A commit listener can see which projection documents a
    commit updated (checked on Fisher), so handlers needn't publish keys at all.

## What the app plugs in

- Each errand: its typed body, its rules, the reason text, and its closed list of offers.
- How long its withdraw window is, if it has one.
- Which errands a thing accepts in which state.
- What starts what ("when a hold is accepted, start _tell me when it's ready_").
- Preferences and delegations: who may ask for whom.
- The channels, and what the messages and apologies say.
- Which projections each errand changes, and the map from projections to read keys.

## On the wire (proposed, not in PROTOCOL)

Errands as resources of their own: `PUT /errands/{id}` with a typed body per errand type
(a discriminator, closed: an unknown type is a 400), `GET /errands/{id}` for its status,
`DELETE /errands/{id}` to withdraw, `GET /errands` for the person's history. Reads stay
resource-oriented. Evidence so far: it separates withdrawing from cancelling, and it makes
`supersedes` unnecessary ([binding a revised quote](errands-insurance.md#bind-a-quote)).
It changes PROTOCOL §1, which today prefers `PUT` to the resource's own URL.

## Types, or spec?

Three levels of real: **it exists** (a named type), **it's checked** (a test asserts it),
or **neither** (not yet, or bullshit).

| Claim                                              | Where                                                        |
| -------------------------------------------------- | ------------------------------------------------------------ |
| An errand ends once, in one of three outcomes      | Types                                                        |
| A decline carries a reason                         | Types                                                        |
| An errand's offers, and a thing's errands by state | Types, reported, so a spec's lists can be compared           |
| What starts what                                   | Both: the link is code, checked by a test; the story is spec |
| Why we chose this, and what we declined            | Spec only                                                    |

## Still straining

- **Partial stock.** "Anything less is a decline" turns one missing item into a round
  trip. It holds if the cart warns early and "ship what you have" can be a preference.
- **Promised messages need a home.** Rules 3 and 9 need a durable way to tell a person
  something that isn't an outcome. Nothing like it exists yet.
- **The wire change** is evidenced by one domain (insurance) and argued from two.
