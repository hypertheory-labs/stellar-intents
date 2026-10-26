# How much the Edge should know: one errand, written twice

**2026-10-26.** Sketches, not code. Every type and method name below is invented for the
comparison, and none of it compiles. The question is whether the Edge is the **counter**
(it checks your session and passes things on) or the **clerk** (it knows what you asked for
and what it can offer when the answer is no).

**Errand** is the word on trial here: what the Edge agrees to take on for a person,
knowing the answer may be no. The person has an intent; the Edge runs the errand. It
stays only if a type ends up named after it.

## The case

Place a hold, declined because the patron already has three, with a remedy.

There's one thing worth noticing before either version. **Today's remedies are all about the
transport**: `try-again`, `wait`, `no`, `sign-in` (PROTOCOL, Remedies). None of them is
about the library. The clerk's remedy here is new: _"You have three holds. Want to swap
one of them for this?"_

I picked swap over "another branch has a copy" because swap needs only data the system
already has (the patron's holds). "Another branch" would need a catalog we'd have to make
up. Swap is also the sharper test, because accepting it takes **two changes that have to
happen together**: cancel one hold, place another.

## Today, for reference

There's no Edge. `EvaluateHoldHandler` applies the library's rule _and_ emits
`IntentDecided` to the browser. The hold aggregate carries the errand's lifecycle:
`Requested` and `Rejected` are hold statuses, though the code itself says a rejected hold
"was never placed".

## Version A: the counter

The Edge checks the session, forwards, and relays. The domain decides and says what can
be done next, because nobody else can.

```csharp
// Edge: generic, knows nothing about holds.
app.MapCounter("/holds/{id}", forwardTo: holdsService);   // session -> patron id, Idempotency-Key, 202
// Domain events with a protocol shape are relayed to the patron's SSE group.

// Domain: the rule, plus what to offer, in protocol terms.
public static OutgoingMessages Handle(EvaluateHold command, IDocumentSession session)
{
    ...
    if (activeOthers.Count >= HoldLimit)
    {
        session.Events.Append(hold.Id, new HoldRejected(...));
        messages.Add(ProtocolEvents.IntentDecided(hold.PatronId, hold.Id, Declined, reason,
            offers: activeOthers.Select(h => new Offer("swap", $"/holds/{h.Id}"))));
    }
}
```

The browser takes up the swap by sending `DELETE /holds/{other}`, then `PUT /holds/{new}`
again. That's two commands. If the delete commits and the place doesn't (a fourth hold
landed from another tab in between), the person gave up a hold for nothing, and **the
browser is where that has to be noticed and apologized for**. That's the "Lego bricks in
the least trustworthy place" problem from the README, back again.

## Version B: the clerk

The Edge owns the errand: it's pending at the Edge, decided at the Edge, and its remedies
are offered by the Edge. The domain knows holds and nothing else.

```csharp
// Domain: no protocol, no patrons' sessions, no "requested".
public abstract record PlaceResult;
public record Placed(Hold Hold) : PlaceResult;
public record OverLimit(IReadOnlyList<Hold> Active) : PlaceResult;

public static PlaceResult Place(PatronHolds patron, TitleId title, BranchCode branch);
public static PlaceResult Swap(PatronHolds patron, HoldId giveUp, TitleId title, BranchCode branch); // one transaction

// Edge, written by the app author: what happens when.
public class PlaceHoldErrand : IErrand<PlaceHold>
{
    public async Task<Decision> Decide(PlaceHold ask, Patron patron, Holds holds) =>
        await holds.Place(patron, ask.TitleId, ask.BranchCode) switch
        {
            Placed => Decision.Accepted,
            OverLimit over => Decision.Declined($"You already have {over.Active.Count} holds.")
                .Offer(over.Active.Select(h => new SwapHold(h.Id, ask.TitleId, ask.BranchCode))),
        };
}

// The offer is just another errand the person can choose, decided the same way.
public class SwapHoldErrand : IErrand<SwapHold> { ... holds.Swap(...) ... }
```

The library (the generic part of the Edge) does everything else: the idempotent PUT, the
202, keeping the errand pending in the session, the decision coming later, the
outcome sent after commit, withdrawn-if-cancelled-first, and the offer sent as something
the browser can take up with one request.

The hold aggregate gets smaller: `Placed` and `Cancelled` only. `Requested` and `Rejected`
belong to the errand, not the hold.

## The four questions

| Question                         | Counter (A)                                                                                            | Clerk (B)                                                                                                      |
| -------------------------------- | ------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------- |
| Where does the remedy come from? | The domain, in protocol terms.                                                                         | The app's errand at the Edge, from a plain domain result.                                                      |
| Who owns "pending"?              | The hold (`Requested`).                                                                                | The errand, in the session. Sessions as a record of intentions come along with it.                             |
| Does the domain import protocol? | **Yes.** `IntentDecided`, offers, patron groups.                                                       | **No.**                                                                                                        |
| What does the app author write?  | The rule, the reason, the offers in wire shape, and every invalidation, mixed into the domain handler. | The rule (domain); a `Decide` per errand with the reason and offers (Edge). Invalidations: unclear, see below. |

## What I think this shows

- **The counter fails question 3,** and that was the disqualifying one. "You plug in what
  happens when" can't be true if what happens when is written in the domain using our wire's
  types. As a library, A is a relay with opinions about your aggregates.
- **The clerk's real cost is that the domain has to offer operations shaped for people.**
  `Swap` exists only because a clerk wanted to offer it. That's arguably correct (the
  library does let you trade a hold), but it means the Edge's needs reach into the domain's
  API, even though its types don't.
- **Remedies split in two.** Transport remedies (`try-again`, `sign-in`) belong to the
  library and are the same in every app. Domain remedies are _offers_: errands the person
  can choose next. Those are app code. One word may be doing two jobs.

## What this doesn't settle

- **Invalidations.** In B, who knows that placing a hold makes `/holds?titleId=…` stale?
  The errand doesn't touch reads. The domain shouldn't know about URLs. That leaves a third
  place, or deriving them (the "LATER" in `HoldKeys`). Unanswered here.
- **What's generic and what's holds.** One domain can't say. Writing quote → bind the
  clerk's way, then diffing, is the next step ([quotes.md](quotes.md)).
- **Where the Edge runs.** Here it's a seam in one process, not a deployment.
