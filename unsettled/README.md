# unsettled: not true yet

Ideas that need to outlast a conversation but can't be said with confidence. Nothing
here is built, and nothing here is a decision. [`docs/`](../docs) says what the system
does; this folder says what we've wondered.

Each thing here should earn its way into `docs/` (by being built) or be deleted. Git
history keeps what's deleted.

| File                                           | What                                                                                                                      |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| [errands.md](errands.md)                       | **Read first.** The shape three sample domains share: eleven rules, what the app plugs in, the wire proposal.             |
| [protocol-proposed.md](protocol-proposed.md)   | The protocol's unbuilt parts: more remedies, receipts per phase, more durability.                                         |
| [specifying-intents.md](specifying-intents.md) | IRMTO, a way to write an intent down. Tried twice, below.                                                                 |
| [holds-in-irmto.md](holds-in-irmto.md)         | The reference's two intents in IRMTO. It found a real bug (a cancel overtaking a hold).                                   |
| [quotes.md](quotes.md)                         | Quote → bind, a second domain, specified before building, with predictions.                                               |
| [backplane.md](backplane.md)                   | Edge.Sse across more than one instance: why it should be easy, the one rule, Postgres `NOTIFY`.                           |
| [edge-weight.md](edge-weight.md)               | One errand (place a hold, over the limit, offer a swap) written as a counter Edge and a clerk Edge.                       |
| [errands-library.md](errands-library.md)       | Library errands, the first of three sample domains: holds, swaps, notifications, inter-library loans.                     |
| [errands-insurance.md](errands-insurance.md)   | Insurance errands, the second sample domain: coverage with a "best we can do" offer, binding a quote, removing a vehicle. |
| [errands-shop.md](errands-shop.md)             | Shop errands, the third sample domain: a grace window, errands addressed to an order, a broken promise as an apology.     |
