import { covers } from './invalidation';

/*
  The read cache, without a framework: memory only (the server's data, never candidates),
  keyed by read key (PROTOCOL §4), shared by everyone reading the same key.

  When is a cached value fresh? Only while we can hear about changes. An entry fetched
  while the event stream is live stays fresh until an invalidation covers it. Anything
  fetched while not live, or held across a disconnect, is stale: it's shown (stale-first,
  with its fetchedAt) and refetched when someone is looking at it. No timers, no staleTime:
  freshness comes from the stream, or it isn't assumed.
*/

/** One read, as the cache holds it. A memory: what the server said, at `fetchedAt`. */
export interface ReadEntry<T = unknown> {
  readonly hasValue: boolean;
  readonly value: T | undefined;
  /** When the value was fetched. Epoch ms. */
  readonly fetchedAt: number | undefined;
  /** Shown, but due a refetch (invalidated, or not fetched while live). */
  readonly stale: boolean;
  readonly fetching: boolean;
  /** The last fetch failed. The value, if any, is still the last one the server gave. */
  readonly error: unknown;
}

const EMPTY: ReadEntry = {
  hasValue: false,
  value: undefined,
  fetchedAt: undefined,
  stale: true,
  fetching: false,
  error: undefined,
};

type Listener = (entry: ReadEntry) => void;

interface Slot {
  entry: ReadEntry;
  /** Who is looking at it ("on screen"). Only watched entries refetch on invalidation. */
  readonly watchers: Set<Listener>;
  inFlight?: Promise<void>;
  /** Invalidated while a fetch was in flight: that fetch may carry the old value. */
  invalidatedInFlight: boolean;
}

export class ReadCache {
  private readonly slots = new Map<string, Slot>();
  private live = false;

  constructor(
    private readonly fetchRead: (key: string) => Promise<unknown>,
    private readonly now: () => number = Date.now,
  ) {}

  /** The entry as it stands, without watching or fetching. */
  peek<T>(key: string): ReadEntry<T> {
    return (this.slots.get(key)?.entry ?? EMPTY) as ReadEntry<T>;
  }

  /**
   * Start looking at a read: the listener gets the entry now and on every change, and a
   * missing or stale value is fetched. Returns the way to stop looking.
   */
  watch<T>(key: string, listener: (entry: ReadEntry<T>) => void): () => void {
    const slot = this.slot(key);
    slot.watchers.add(listener as Listener);
    listener(slot.entry as ReadEntry<T>);
    if (slot.entry.stale) void this.refresh(key);
    return () => slot.watchers.delete(listener as Listener);
  }

  /** Fetch now. One request per key at a time: a second caller shares the first. */
  refresh(key: string): Promise<void> {
    const slot = this.slot(key);
    if (slot.inFlight) return slot.inFlight;

    const liveWhenAsked = this.live;
    this.update(slot, { fetching: true });
    slot.inFlight = this.fetchRead(key)
      .then(
        (value) =>
          this.update(slot, {
            hasValue: true,
            value,
            fetchedAt: this.now(),
            // fresh only if live throughout, and nothing invalidated it meanwhile
            stale: !(liveWhenAsked && this.live) || slot.invalidatedInFlight,
            fetching: false,
            error: undefined,
          }),
        (error: unknown) => this.update(slot, { fetching: false, error }),
      )
      .finally(() => {
        slot.inFlight = undefined;
        slot.invalidatedInFlight = false;
      });
    return slot.inFlight;
  }

  /** PROTOCOL §3: reads covered by any of these keys are stale; the watched ones refetch. */
  invalidate(keys: readonly string[]) {
    for (const [readKey, slot] of this.slots) {
      if (keys.some((key) => covers(key, readKey))) this.markStale(readKey, slot);
    }
  }

  /** Everything is stale; the watched ones refetch. */
  invalidateAll() {
    for (const [readKey, slot] of this.slots) this.markStale(readKey, slot);
  }

  /**
   * Whether we can hear about changes (the stream is connected). Going live re-checks
   * everything (PROTOCOL §3, the subscribed rule); going quiet means nothing is fresh.
   */
  setLive(live: boolean) {
    this.live = live;
    if (live) this.invalidateAll();
    else for (const slot of this.slots.values()) this.update(slot, { stale: true });
  }

  private markStale(key: string, slot: Slot) {
    this.update(slot, { stale: true });
    if (slot.inFlight) {
      // It may carry the old value: distrust its answer, and ask again once it lands.
      slot.invalidatedInFlight = true;
      if (slot.watchers.size > 0) void slot.inFlight.then(() => this.refresh(key));
      return;
    }
    if (slot.watchers.size > 0) void this.refresh(key);
  }

  private slot(key: string): Slot {
    let slot = this.slots.get(key);
    if (!slot) {
      slot = { entry: EMPTY, watchers: new Set(), invalidatedInFlight: false };
      this.slots.set(key, slot);
    }
    return slot;
  }

  private update(slot: Slot, changes: Partial<ReadEntry>) {
    slot.entry = { ...slot.entry, ...changes };
    for (const listener of slot.watchers) listener(slot.entry);
  }
}
