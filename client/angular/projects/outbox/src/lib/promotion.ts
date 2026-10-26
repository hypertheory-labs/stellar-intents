import { Candidate, Intent } from './types';

/** True for a guess, false for the server's version. Narrows the type for templates. */
export function isCandidate<T>(item: T | Candidate<T>): item is Candidate<T> {
  return typeof item === 'object' && item !== null && (item as Candidate<T>).kind === 'candidate';
}

/**
 * The id the real item has, or will have: the intent's own (the client made it, PROTOCOL
 * §1), unless the outcome said it made something with another id (§3, `made`).
 */
export function realIdOf(intent: Intent): string {
  const state = intent.state;
  return (state.status === 'decided' && state.outcome === 'accepted' && state.made) || intent.id;
}

/**
 * What the screen shows: the server's data (memory), plus the guesses whose real version
 * hasn't arrived yet.
 *
 * Memory is never edited. A candidate is dropped the moment memory contains an item with its
 * id (see `realIdOf`), so "has the server's version arrived?" is an exact check: the guess
 * and the real thing never both show, and nothing flickers out and back. Candidates come
 * first: they're the newest things the user did.
 *
 * WHY an id and not "the next read after the outcome": `invalidate` can arrive before
 * `intent-decided`, so a read can show the real item while the guess is still showing.
 */
export function mergeWithCandidates<T>(
  memory: readonly T[],
  candidates: readonly Candidate<T>[],
  idOf: (item: T) => string,
): (T | Candidate<T>)[] {
  const known = new Set(memory.map(idOf));
  return [...candidates.filter((c) => !known.has(c.id)), ...memory];
}

/**
 * The ids of the intents ready to be retired (promoted): of these decided intents, the
 * ones whose real item is in memory. Retiring is still the caller's explicit act; this only
 * says which ones are ready.
 */
export function readyToRetire<T>(
  done: readonly Intent[],
  memory: readonly T[],
  idOf: (item: T) => string,
): string[] {
  const known = new Set(memory.map(idOf));
  return done.filter((intent) => known.has(realIdOf(intent))).map((intent) => intent.id);
}
