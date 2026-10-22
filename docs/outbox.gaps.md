# The Outbox: What It Doesn't Answer Yet

*Beside [`outbox.md`](outbox.md): the places where this course's outbox stops, written as questions. They're unsettled on purpose, and good ones to argue about. If your team settles one, you've written a requirement.*

---

### 1. What do we do with "unknown"?

A timed-out request may or may not have arrived. We could ask the server (`GET /holds/{id}`), but if the answer is `404 Not Found`, does that mean *"it never arrived"* or *"it hasn't arrived yet"*? When is it safe to tell the user it didn't happen? Today the outbox says "we're not sure" and keeps listening.

### 2. What does "Try again" send?

After a rejection ("you already have 3 holds"), sending the same id again just gets the same rejection back; that's what the id is for. So a retry after a decision is a *new* request. But after "unknown," resending the *same* id is the safe move. Who decides which, and does the user need to know the difference?

### 3. What happens when the user reloads?

Today, a reload forgets every intent. The hold may still be placed, but the receipt, the guess, and any apology are gone. What should survive a reload, and where would it be kept? What happens when an old intent is found the next morning?

### 4. Is the request still what they meant?

If an intent sat unsent for an hour (offline, a closed laptop), should it go out automatically when the connection comes back? "Safe to send twice" isn't the same as "still wanted." When must the app ask again first?

### 5. Two tabs

Each tab has its own outbox. A hold placed in one tab shows up in the other's list on its next reload, but the other tab never saw the receipt or the guess. Does it need to? Which tab owns the apology?

### 6. Does order matter between requests?

The outbox sends every intent right away, in any order. For one hold, that's fine. But a patron at the 3-hold limit who cancels one hold and then places another needs the cancel to land first, or the new hold is rejected. Which of your requests must go in order, and who would know?
