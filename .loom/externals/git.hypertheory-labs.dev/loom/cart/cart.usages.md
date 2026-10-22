# How this repository uses cart.md

Roles cast 2026-10-22, from fallbacks Jeff accepted ("please start that cart").

- **Where a cart lives:** `.loom/cart/`, ignored by git.
- **Durable record for declines:** `.loom/event-log.md`, committed. The cart's
  `event-log.wal.md` is not durable.
- **Isolation while a cart is open:** a local branch, named after the cart.
- **The two presences:** `jeff` (the owner of the code) and `claude` (everyone else).
  Both are stakes: whoever fills the seat next keeps the file.
- **Who decides when to convert:** jeff. Claude can't feel a cart filling up.
