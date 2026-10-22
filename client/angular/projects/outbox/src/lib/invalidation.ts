import { type } from '@ngrx/signals';
import { eventGroup } from '@ngrx/signals/events';

/*
  One vocabulary for "these reads are stale", from either side.

  - `from: 'server'`: the event stream's `invalidate` (PROTOCOL §3). Covers changes anyone
    made, including other users.
  - `from: 'intent'`: an intent type's own `invalidates` declaration, when the server has
    recorded the intent. Covers this tab, immediately, with or without a stream.

  Same keys, same matching, same listener: a read cache can't tell (and shouldn't care)
  which side said it.
*/

export const invalidationEvents = eventGroup({
  source: 'Reads',
  events: {
    invalidated: type<{ keys: readonly string[]; from: 'server' | 'intent' }>(),
  },
});

/**
 * Whether an invalidation key covers a read key (PROTOCOL §3): equal, or a prefix that ends
 * at a segment boundary. `/holds` covers `/holds`, `/holds?patronId=p1`, and `/holds/0b6e…`,
 * but not `/holdsArchive`. A query string only ever matches exactly.
 */
export function covers(key: string, readKey: string): boolean {
  if (readKey === key) return true;
  if (key.includes('?') || !readKey.startsWith(key)) return false;
  if (key.endsWith('/')) return true; // already at a boundary: `/` covers everything
  const next = readKey.charAt(key.length);
  return next === '/' || next === '?';
}
