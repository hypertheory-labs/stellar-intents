# The reference domain's intents, specified

**2026-10-23.** The two intents the reference already has, written in the
[IRMTO form](../SPECIFYING-INTENTS.md) and then checked against the code. Each line says
where it lives, or ⚠ when it has nowhere to live. An experiment: does the form find
things the code hides?

## Place a hold

- **Intent:** Patron → Library: place a hold on {title} at {branch}.
  → `placeHold` (demo `holds.ts`): `PUT /holds/{intentId}`. The intent's id is the hold's.
- **Rests on:**
  - who's asking: the session. → `[Authorize]`, the patron comes from the cookie.
  - acting for whom: themselves only. ⚠ A librarian can't place a hold _for_ a patron;
    "on behalf of" isn't modelled.
  - the title exists, and the branch exists. ⚠ Nothing checks either. Any `titleId` is
    accepted.
  - the patron is in good standing. ⚠ Not modelled.
- **Manifests when:** the patron has fewer than 3 placed holds when it's decided, and the
  hold is still requested.
  → `EvaluateHoldHandler`, about 3 s later (a scheduled message).
  ⚠ Two decisions for one patron can race past the limit (the event log has it; DCB is
  the candidate fix).
- **Then:** the hold is placed (`HoldPlaced`).
  → `IntentDecided: accepted`. Invalidates the hold, the patron's list, and the title's wait
  list (`HoldKeys`).
  **Tell:** the patron (the person). → `IntentDecided` is addressed to the patron, but ⚠
  it's only delivered to tabs open at the time. With none open, only a later status check
  finds out, and nobody tells the patron "your hold went through".
- **Otherwise:**
  - at the limit → `no`: "You already have 3 holds, the limit." → `HoldRejected`.
    ⚠ No offer. **Library → Patron:** "cancel one of them?" is the obvious one.
  - the decision itself fails (an exception, retries used up) → ⚠ **nothing.** The
    message is dead-lettered (Wolverine's default once its retries run out), and the patron's intent sits in `received` with no word.
    Nobody escalates. (**Library → Librarian** would be the shape.)
  - signed out → `sign-in` → `interrupted`, same id.
  - the same id with a different body, or another patron's → `no` (409).
  - **the patron cancels it first** (another intent, same hold): withdrawn. **Tell:**
    this browser, quietly. Before the fix → ⚠ **nothing.** The decision finds the hold
    cancelled and returns without a word. No `IntentDecided` is sent, and the status
    check answers `cancelled`, which the intent type doesn't map. So the intent sits in
    `received` forever. Worse, because the cancelled hold leaves the patron's list, the
    place-hold candidate **reappears as a guess**. Confirmed in the browser.
    **Fixed** (`b10ab10` and the demo commit after it): the decision now sends an outcome,
    `withdrawn` since the outcomes were named, and the demo dismisses the placement when
    the cancel lands. ⚠ Only the demo knows the cancel overtakes the placement, and only
    because a create's intent id is the hold's.

## Cancel a hold

- **Intent:** Patron → Library: cancel my hold on {title}.
  → `cancelHold`: `DELETE /holds/{holdId}`, with a **new** intent id as its
  `Idempotency-Key`. Not a create, so the intent id isn't the hold's.
- **Rests on:**
  - who's asking. → the session; another patron's hold is a 403.
  - the hold exists and is theirs. → 404, 403.
- **Manifests when:** the hold isn't rejected. → decided inside the request (no `202`).
- **Then:** the hold is cancelled (`HoldCancelled`).
  → `204`, so accepted at once. Invalidates the same three reads.
  **Tell:** this browser (the `204`). The patron's other tabs only see the hold leave
  their list. That's enough, because nothing is pending there.
- **Otherwise:**
  - rejected → `no`: "This hold was never placed, so there's nothing to cancel." (409)
  - already cancelled → accepted (a repeat is a success).
  - no such hold → `no` (404, with no `detail`, so the apology uses its own words).
  - another patron's → `no` (403).
  - **no answer in time** → `unknown`, and ⚠ **forever.** The type declares no status
    check, and the server never sends `IntentDecided` for a cancel, so nothing can settle
    it. Yet the read model that would confirm it **exists**: `GET /holds/{holdId}`
    answers `status: cancelled`. It just wasn't declared. **Fixed:** the type now declares
    it.
- ⚠ **The server never records the cancel's intent id.** `DELETE` is idempotent by
  nature, so it doesn't need to. But it means "what became of intent X?" has no answer
  for a cancel. That's the ledger question (shapes A/B/C), arrived at from the spec.

## What the form found

1. **A real bug:** one intent changes the outcome of another (a cancel overtakes a
   placement), and the first is never told. Neither intent's spec alone shows it. Only
   writing both side by side does. The _Otherwise_ slot is what asks the question.
2. **A confirmation that exists but isn't declared** (cancel's status check). I bet the
   spec would show cancel had no confirming read model, and I lost: `GET /holds/{id}`
   confirms it. The intent type just doesn't use it.
3. **Bases nobody checks** (the title, the branch, good standing). _Rests on_ makes them
   visible. Whether they matter is the business's call.
4. **Apologies with no offer**, where one is obvious ("cancel one of them").
5. **With arrows and addresses** (added after): a decision that's addressed to the
   person but delivered only to open tabs; a dead-lettered decision that nobody hears
   about; and no "on behalf of" at all.
