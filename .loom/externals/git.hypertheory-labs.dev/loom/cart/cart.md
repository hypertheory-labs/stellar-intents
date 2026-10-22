# Agreement — the cart

**v2.** Depends on `annotating/v0`.

**How two parties work out what something means before one of them changes it.**

---

## Why

An AI has no good way to say *"I need to know more from you before I do this, and
I suspect you don't know either, and I don't want to make you look stupid."*
Neither do most people — **they assume the lightbulb will arrive once enough
content has been offloaded on them.**

**The cart gives that sentence somewhere to go where it does not read as an
obstruction.**

### Say what you will do if nobody answers

**Every open item states its own fallback.** *"If unanswered I will store an
opaque string and not link it."*

**This is what stops the cart from stalling.** Without it an unanswered question
is an unresolved obligation and work stops; with it, **silence is a usable
reply** and the round can close with items still open in a way both parties
understand.

> **Silence means proceed. It never means dropped.**

*A fallback that quietly becomes the decision is worth knowing about later, so it
is worth being able to find them.*

## The cart is the round, not the folder

**A cart is one bounded exchange.** It starts, it runs, it **converts**, and then
it is gone. The directory is only where a cart happens.

**What ends a cart is size, not time.**

> **A cart converts when it exceeds what two people can hold at once.**

*A whiteboard's virtue is that it is bounded and erasable: when it fills, you have
to decide what matters. Cover the walls and you have removed the only thing it
was doing for you.*

**A cart that cannot convert and a commit that cannot be reviewed are the same
failure.** The sphere got bigger than a head.

## Two files. Only ever two.

**One daily per presence.** One for the owner of the code. One for **everyone
else**, collapsed into a single voice.

**A presence is not a person.** Three people on the outside speak as one, or they
do not speak — and **they do their disagreeing somewhere else first, in their own
cart, where they are the two parties.** Carts nest by party-pair, not by topic.

**There is never a third file.** `jill-daily.md`, `bill-daily.md`,
`im-the-ceo.md` is a chat log; a chat log has no bound; **something with no bound
never converts.**

**The test, when someone wants a third:** *what happens to this file when the
person changes?* **If it transfers, it was a stake. If it dies, it was a person.**

*If someone with authority wants in: have them write it as a venue constraint for
the org instead. **An authority claim entering as a venue fact carries a reason
and can be shown wrong. Entering as a voice in a cart, it cannot.***

## Only dailies get annotated

**Anything else you put in someone's cart is a specimen.**

> **Specimen** — evidence. *Look at this; it may bear on your decision.* It
> belongs to the repository the cart lives in, **and they may throw it away
> without asking you.** Default: discard.

> **Polad** — a candidate artifact, shaped exactly like what it would become,
> staged so you can see whether it fits. Default: apply or discard.

**Promotion is explicit.** A specimen cannot drift into a kept file — **if you
want to keep it, it must be re-shaped as a polad and placed deliberately.**
*Otherwise `docs/` fills with things nobody threw away.*

**A specimen cannot travel between carts.** If it still matters in the next one,
**it is reintroduced, with a fresh reason** — *"this is still here because of line
1,237."* Anything that travels silently accrues standing nobody re-affirmed.

**So responding to a specimen means restating it in your own daily, in your own
words.** That costs precision and buys something better:

> **Pinning a defect in place lets you be exact without having understood.
> Restating surfaces the misunderstanding that pinning hides.**

## Converting destroys, so extract first

**The cart is not committed. So conversion does not archive it — it deletes it.**
*Every earlier version of this agreement was wrong about that by accident: a
committed cart left its dailies in history, and "gone" was only ever true of the
tree.*

> **At conversion the cart is the only copy of everything in it.** *The dailies,
> the annotations, the answers to every prompt, and the write-ahead log.*

**So before converting, extract:**

- **every answer to a `Question`, `Select` or `Affirm`** — *an answer lands in a
  notes file, a notes file dies at dissolve, and an untracked cart has no history
  to fall back on.* **A prompt answered and not extracted was never answered.**
- **every write-ahead entry that survived** — *promote or discard, and both are
  acts.*
- **every fallback that quietly became the decision** — *say so in the entry, in
  those words.*

**Staging as you go turns conversion into a move rather than a rewrite.** *Keep
the write-ahead log current during the round and there is nothing to reconstruct
at the end, which is when you have least appetite for it.*

*Two rules in this agreement now both push toward loss — **act as if the shelf is
discarded daily**, and **the cart is not committed.** They are both right and
together they mean **nothing in a cart is safe by default.***

## Conversion emits two things

**The artifact** — the code, the decision, the change — **and the declines.**

> **A decline is what you considered and did not do, with the reason you did
> not.**

**Almost nobody writes these.** Three weeks later someone proposes the thing you
already rejected, and the rejection is gone because it lived in an annotation
that died with the round.

**A decline needs no file of its own.** It is an entry in whatever durable record
you keep — **which must outlive the cart**, *and therefore cannot be inside it* — and **it should say what you believed, not just what you chose** — only
a belief can later be shown wrong.

## The cart is local, and that is what keeps it to two files

**A cart is two developers working side by side.** *Everything else — the wider
team, the people who need to know, the thing that has to be tracked — is issues,
chat, whatever you already have.* **This is not that channel and it does not scale
into one.**

> **So the cart is not committed.** *It lives in the working tree of the machine
> the two presences share, and `.loom/cart/` is ignored by version control.*

**The reason is not tidiness. A committed cart grows a third file by itself.**
*The rule above defends against somebody asking for one; **version control does
not require anybody to ask.*** *Anyone who can clone can add `joe-rose.md`, and
then `sue-rose.md`, and the agreement's defence — **what happens to this file when
the person changes?** — is never invoked, because nobody ever had the
conversation.*

**This is also what makes a round actually end.** *Committed, a cart is gone from
the tree and permanent in history — **so "gone" means "no longer live" and
negotiation stays quotable forever.*** **Ignored, gone means gone**, which is what
the round dying was for.

*The cost, stated: **two presences who do not share a filesystem cannot use a
cart.*** *That is a real limit and it is the right one — if you need a medium
between machines, you need the other channel, and reaching for a cart there is
how it becomes a chat log.*

## What is not yet a decision goes in the write-ahead log

**A round produces things that are neither questions nor decisions:** *something
observed, something that may turn out to be noise, something you would kick
yourself for losing and cannot yet justify writing down.*

**`event-log.wal.md`, in the cart.** *Findings, not decisions.* **Nothing in it is
durable and nothing in it has been decided.**

**At conversion, each entry either becomes an entry in the durable record or is
discarded.** *Same two exits as a polad, and for the same reason: **conversion is
when you know most about it.***

> **Write the reason it is not yet an entry.** *An observation you cannot justify
> promoting is worth keeping; **one you have not said why you are hesitant about
> will be promoted by whoever finds it, on the strength of it having been written
> down.***

## Where a cart lives, and the shelf

**A fixed path, and at most two things in it:**

```
{wherever you put carts}/
├── current/
│   ├── name          one meaningless word
│   ├── {x}-daily.md
│   └── {y}-daily.md
└── shelf/            remnants of the last round, if any
```

**The directory names are fixed on purpose.** A cart directory you can name
becomes `PROJ-1234` within a month, and then the cart *is* a ticket — with a
status, an assignee, and a backlog behind it. **The name goes in a file, where
nothing navigates by it.**

**Three states, visible in a listing:** no `current/` → no cart. `current/` alone
→ open, last round fully resolved. **`current/` and `shelf/` → carry-forward
pending.**

### Converting: whose act it is, and who decides when

**The cart lives in someone's repository, and it is theirs to reshape and theirs
to convert.** This is not a permission question.

**But converting ends a round the other presence may be standing in**, so:

> **Do not convert while someone is mid-thought. Ask whether they are ready. If
> you convert alone anyway, say so** — and expect them to reintroduce whatever
> they were in the middle of.

*The harm is smaller than it sounds: **the shelf catches what did not resolve**,
so nothing is destroyed. What is taken is the round, and a round is recoverable
by reintroduction with a reason.*

**If a tool offers a convert action, its confirmation should restate this and
nothing more.** A dialog that invents an obligation creates **shadow policy** —
something everyone obeys, nobody agreed to, and which cannot be found or argued
with.

### Deciding *when* belongs to a presence that can feel the bound

**The size trigger assumes someone notices a cart getting full. Not every
presence has that instrument.**

An AI in a cart **will not feel it filling and will keep going until told to
stop** — not from carelessness; there is nothing there to feel it with. **Asking
would produce an answer, and the answer would be fabricated.**

> **When one party cannot feel a cost, the decision belongs to the party who
> can.** Sensor placement, not authority.

*The same reason an agent commits and does not push: the consequences of a push
land on people it cannot experience. **Say who owns the conversion call, in your
notes file, before you need it.***

**If neither presence can feel it** — two agents in a cart with no human — **size
is not an available trigger** and you need a different one. We do not have a good
answer for that case.

### A third trigger, from use

**A cart may want to convert when the ground shifts under it**, at any size —
*not* a change of subject, but a change of the coordinate system: the protocol
itself changed, or a venue moved, and the round you are in was framed by
something that no longer holds.

*Reported by the first adopter, from a cart that was small and still wanted to
end. It may be its own trigger or too rare to be worth naming.*

### The shelf

**One live cart. Ever.** *"Let's switch to another cart"* is how nothing ever
converts.

**The shelf holds what did not resolve when the last cart converted**, and
carrying something forward means **re-authoring it**, not moving it.

> **Act as if the shelf is discarded at least once a day.**

**Nothing enforces that, and it is still the useful rule**, because it sorts
without enforcing: **if you would mind losing it overnight, it was never shelf
material.** It is a polad to apply, an entry to log, or a decline to record.

**Which is why no polad ever goes in the shelf.** A polad has exactly two exits —
**applied or discarded** — and forcing that at conversion is the point, because
conversion is when you know most about it.

**If you find mid-round that you are in the wrong cart**, move it out of here
entirely — **not to the shelf**, which is a carry-forward slot with nothing to
carry into. Anything from it that still matters is **reintroduced later with a
fresh reason.** *And it owes one sentence: what the conversation turned out to
be. Not a justification — the name of the round you were actually in.*

## The cart's name

**Give the cart a meaningless name.** A uuid, or `watermelon`. **Meaningless on
purpose**, so it cannot accrue a category.

**Tag durable entries with it.** The tag says exactly one thing:

> **Everything tagged with this name was considered in a single sphere of
> understanding.**

**The cart is gone by then.** You keep the boundary without keeping the container
— so you can later ask *what else was in the room when we decided this*, which is
the question someone who was not there actually asks.

---

## Roles you must cast

**An agreement adopted with these uncast has not been adopted. It has been
filed.**

## Select: Where does a cart live?
- `.loom/cart/`
- somewhere else under `.loom/`
- outside `.loom/` entirely
- there is no directory; carts happen somewhere that is not the repository

## Select: What is the durable record that declines go into?
- an event log file in the repository
- ADRs
- commit trailers
- an issue tracker
- nothing yet — declines are written and lost, knowingly

## Select: What isolates work in progress while a cart is open?
- a local branch
- a worktree
- a separate clone
- nothing; work happens in place

## Question: Who are the two presences, and what happens to each file if that person leaves?

*Name them. If either answer is "the file dies," you have a person and not a
stake, and the cart will grow a third file within a month.*

---

## What we know is unsettled

**Whether size is the only conversion trigger.** Size is what has been observed —
a cart might also want to convert on a **change of subject** at small size, and
there is no example either way.

**Whether `specimen` and `polad` stay two roles.** They are two here because
promotion is a deliberate act. If specimens routinely turn out to persist without
anyone promoting them, that is one role with a discovered property, and this is
wrong.
