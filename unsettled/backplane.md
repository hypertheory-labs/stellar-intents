# A backplane for Edge.Sse

**2026-10-24.** Not built. `SseHub` is per-process: with two instances, a browser connected
to one never hears events published on the other. Planned for the move to Postgres, which
is also when Aspire makes a two-instance test practical. Built for our needs, not anyone
else's yet.

## Why it should be easy here

- **No replay.** An instance that misses something ends its streams, and the browser's
  `subscribed` re-check covers the gap. So each instance needs only a throwaway queue that
  dies with it: nothing to persist or catch up on.
- **Order doesn't matter.** Outcomes are final and invalidations are idempotent, so events
  arriving in any order across instances are fine.
- **After commit is already handled.** Wolverine's outbox gets the message to the
  backplane only after commit.

The shape: `ServerSentEvent` is broadcast to every instance, and each instance's local
handler feeds its own hub, which already filters by group.

## The one rule

**Losing the backplane ends every stream.** An instance cut off from it for ten seconds
would otherwise miss events silently, which the hub already refuses to allow when a
connection's channel fills. Same rule: end the streams, and browsers reconnect and
re-check.

Unknown: whether Wolverine exposes "this listener lost its connection" cleanly. Look that
up first.

## Postgres `NOTIFY`

`NOTIFY` is transactional: it's delivered only when its transaction commits, which is our
after-commit guarantee built into the database. Each instance `LISTEN`s on one channel.
Limits: payloads under about 8 KB, and a disconnected listener misses notifications (the
rule above). Unknown: whether Wolverine already has something built on it. If not, a
listener feeding the hub may be all it takes, with no broker.

## Done when

A test with two app instances sharing the backplane, a browser stream on each, shows an
event published on one reaching the other, and a lost backplane ending the streams.
