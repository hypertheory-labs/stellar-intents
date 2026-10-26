import { type } from '@ngrx/signals';
import { eventGroup } from '@ngrx/signals/events';

/*
  "These reads are stale": the event stream's `invalidate` (PROTOCOL §3), for changes
  anyone made, including other users. Only the server says it: the server knows what a
  commit wrote, and an intent type would only be guessing.
*/

export const invalidationEvents = eventGroup({
  source: 'Reads',
  events: {
    invalidated: type<{ keys: readonly string[] }>(),
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
