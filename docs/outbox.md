# The Outbox, Briefly

*For reading later: the ideas behind the Day 3 outbox, then a short tour of how this course's code does it.*

---

## The ideas

### A request is not a fact

"Please place a hold" is true the moment the user clicks. "Your hold is placed" becomes true later, maybe much later, and maybe never. Most bugs in this area come from a screen that treats the first sentence as the second.

### Say something true, at every moment

There's always a true sentence. Right after the click: *"We've received your request to place a hold on Dune."* That's a **receipt**. It stays true no matter what happens next, so it's always safe to show.

**Received is not done.** A server that answers `202 Accepted` is telling you exactly that: it has your request, and the outcome is later.

### Keep guesses apart from memory

Everything the app holds from a server is a **memory**: what the server said, at some moment. A **guess** is something the screen shows before the server confirms it ("your new hold, at the top of the list").

The mistake is to write the guess *into* the memory, then try to take it back out if it was wrong. Keep them separate instead. The screen shows the server's data plus the guesses that haven't been confirmed yet, and **a guess looks like a guess** (dimmed, labelled "not confirmed yet"). When the real item arrives, the guess is dropped. If the guess was wrong, nothing else on the screen changes, because nothing else was touched.

### If you can't describe the apology, you haven't earned the guess

An **apology** is what the user is told when the guess didn't hold: *"We couldn't place your hold on Dune: you already have 3 holds, the limit."* Write it before you write the guess. If it's hard to write, maybe don't guess; show only the receipt.

### One id per intent, created once

The user's request (the **intent**) gets an id the moment it's formed, created in the browser. That id goes with every send, so if the request is sent twice (a retry, a double click, a flaky network) the server can tell it's the same request, not a second one. The server's record uses the same id, so "has the real version arrived yet?" is an exact check.

### "No answer" is not "failed"

If the server doesn't answer in time, the honest state is **unknown**: it may have arrived. Treating a timeout as a failure invites the user to try again, and that's how people get two of something.

### Hearing about "done" is a ladder

How does the screen find out the outcome? In rungs, each one added on top of the last:

1. A **"Check status"** button: ask the server.
2. **Check when the user comes back** to the tab.
3. The server **tells** you (server-sent events).

Each rung makes the page quicker to know; none replaces the one below. If the server's event stream is down, the page still works, just less promptly.

### Once it's decided, it's decided

Answers can arrive out of order: a slow response can land after an event already said "done." Once an outcome is known, a late answer must not undo it.

---

## How this course's code does it

The library is `projects/outbox` (its README has the full reference); the page that uses it is **Place a hold (outbox)** in the reference app (shown in class; you get the whole app at the end of the course).

- **The intent type** declares, in one place, the request to send, the receipt, the guess, and the apology. Intents themselves are plain data.
- **Command in:** the page calls `enqueue(...)`. The outbox creates the id, sends `PUT /holds/{id}`, and tracks the intent: requested, sent, received, then done or needs attention (or unknown, or failed to send).
- **Events out:** each step is published as an event (`[Outbox] intentReceived`, `intentDone`, ...). The page listens and reloads its own list. The outbox doesn't know the page exists.
- **The table** merges the server's holds with the outbox's guesses; a guess is italic and labelled.
- **Promotion:** once the server's row is in the page's data, the page **retires** the intent, and the guess is gone.
- **"What just happened"** under the table logs each step with its time since the click, including how the page heard (asked, or was told).

What it doesn't do yet, and the questions that raises, are in [`outbox.gaps.md`](outbox.gaps.md).
