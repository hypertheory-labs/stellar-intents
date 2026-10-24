# Protocol: proposed, not built

Taken out of [`docs/PROTOCOL.md`](../docs/PROTOCOL.md) on 2026-10-24, when it was cut back
to what's built. These were its `[Proposed]` parts, kept here in case they earn their way
back. Section names refer to the protocol.

## Lifecycle

- **Receipts per phase.** The lifecycle's boxes were called phases ("with us", "with
  them"), and moving between them was custody changing hands. The idea: a receipt changes
  when custody does, one per phase, its words chosen by the business. (Today a receipt is
  one sentence per intent type, and it isn't true in every state.)
- **Which intent a cancel overtakes.** Nothing on the wire says. Today a client can only
  tell when the intent created the thing being cancelled (the same id), and until the
  server's `withdrawn` arrives, the first intent's guess can come back. `supersedes={id}`
  on the new request is the candidate (see [quotes.md](quotes.md)).
- **"Never mind" before it left this browser.** Needs no wire at all. No client offers it.
- **Stuck:** `received` past the time the server promised (a `202` with `Retry-After`).
- **Needs a person:** the server can't decide without its own staff (a dead-lettered
  message). Nothing on the wire can say so. It needs its own event, and its remedy is an
  offer ("let's talk"). It isn't final; a person will decide.

## Identity

- The server **MAY** close the stream at any time, including when the user's access token
  expires. The reconnect goes back through whatever refreshes the session, or comes back
  `401`.

## Reads

- The server **MAY** send `Cache-Control` (e.g. `max-age=30, stale-while-revalidate=300`)
  for reads that tolerate it. The browser's own cache then serves stale-first for free.

## Remedies

A remedy that needs more than a status code to choose would be a problem+json `type`: the
countable part, for a machine. `detail` stays the part for a person.

- `look-again`: the reads the intent was based on have changed; here's what changed.
  Needs intents to say what they were based on, and a problem `type`. Whether the server
  rejects, or accepts anyway within some window, is the business's policy.
- `still-want-it`: a stored intent too old to send.

The principle behind who acts on a remedy:

> The client may deliver what the person asked for, as many times as it takes, while it
> still means what they meant. It may never ask for something new on their behalf.

- Every remedy that needs a **new** id (`no`, `look-again`) needs the person, because a
  new id is a new request.
- While a remedy is handled automatically, there's nothing to apologise for yet: "we'll
  keep trying" is a receipt. It becomes an apology when the automation gives up, or when
  it's the person's turn. (The reference still shows these as apologies.)

## Durability

Paused (see the README): if the Edge holds pending intents, most of this stops mattering.

- On sign-out, **SHOULD** delete that user's stored intents, because the computer may be
  shared.
- Resend (same id) only when the status check answers `404`. (Today it stays `unknown`.)
- **MUST NOT** send a stored intent that was never answered once it's older than its
  intent type says a request stays good. Ask the person instead.
- **SHOULD** let only one tab act on a stored intent at a time.
- **SHOULD** word the receipt of an unanswered intent as "saved on this device", not
  "saved".

Their conformance scenarios, unbuilt:

- As the reload scenario, but the server never got it (`404`) → resent with the same id.
- A stored intent never answered is older than its type's limit → not sent; the person
  is asked.
- Two tabs with the same stored intent → one request goes out, and one receipt shows.
