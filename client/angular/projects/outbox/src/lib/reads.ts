import { HttpClient } from '@angular/common/http';
import {
  computed,
  DestroyRef,
  effect,
  EnvironmentProviders,
  inject,
  InjectionToken,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
  Resource,
  resourceFromSnapshots,
  ResourceSnapshot,
  Service,
  Signal,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Events } from '@ngrx/signals/events';
import { firstValueFrom } from 'rxjs';
import { invalidationEvents } from './invalidation';
import { ReadCache, ReadEntry } from './read-cache';
import { serverEvents } from './stream';

/*
  The read cache, for Angular: shared entries (ReadCache), each reader gets a real
  `Resource` made from them (resourceFromSnapshots, experimental in Angular 22).

  WHY not one `resource()` per reader: a resource owns its loader and its value, so two
  components reading the same key would fetch twice and hold two copies. Here the cache
  owns both, and a resource is just a view of an entry.
*/

export interface ReadsOptions {
  /** Prepended to a read key to make its URL, e.g. `/api`. Read keys start with `/`. */
  readonly apiRoot: string;
}

const READS_OPTIONS = new InjectionToken<ReadsOptions>('read cache options', {
  factory: () => ({ apiRoot: '' }),
});

/** The read cache, listening from app start. Call it in app.config. */
export function provideReads(options: ReadsOptions): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: READS_OPTIONS, useValue: options },
    provideEnvironmentInitializer(() => void inject(Reads)),
  ]);
}

/** The app's one read cache, wired to invalidations and to whether the stream is live. */
@Service()
export class Reads {
  private readonly http = inject(HttpClient);
  private readonly apiRoot = inject(READS_OPTIONS).apiRoot;

  // WHY cache: 'no-cache': this cache decides freshness, so a refetch must reach the
  // server, not the browser's HTTP cache. With an ETag that's a cheap 304. (Needs
  // withFetch(); the XHR backend ignores it.)
  readonly cache = new ReadCache((key) =>
    firstValueFrom(this.http.get(this.apiRoot + key, { cache: 'no-cache' })),
  );

  constructor() {
    const events = inject(Events);
    const destroyRef = inject(DestroyRef);
    events
      .on(invalidationEvents.invalidated)
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(({ payload }) => this.cache.invalidate(payload.keys));
    events
      .on(serverEvents.subscribed)
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.cache.setLive(true));
    events
      .on(serverEvents.disconnected, serverEvents.signedOut)
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.cache.setLive(false));
  }
}

/** A cached read, as a Resource, plus what a memory should be honest about. */
export interface CachedHttpResource<T> extends Resource<T | undefined> {
  /** When the value was fetched (epoch ms): "on the shelf as of 10:42". */
  readonly fetchedAt: Signal<number | undefined>;
  /** Shown, but due a refetch. */
  readonly stale: Signal<boolean>;
  /** The last refetch failed. The value is still the last one the server gave. */
  readonly refreshError: Signal<unknown>;
  /** Fetch it again now. */
  reload(): void;
}

const NOTHING: ReadEntry<never> = {
  hasValue: false,
  value: undefined,
  fetchedAt: undefined,
  stale: true,
  fetching: false,
  error: undefined,
};

/**
 * Reads a key through the cache: `cachedHttpResource(() => `/holds?patronId=${id()}`)`.
 * `undefined` reads nothing (idle). Call it in an injection context. While a component
 * holds it, the read is "on screen": invalidations refetch it.
 */
export function cachedHttpResource<T>(key: () => string | undefined): CachedHttpResource<T> {
  const reads = inject(Reads);
  const readKey = computed(key);
  const entry = signal<ReadEntry<T>>(NOTHING);

  effect((onCleanup) => {
    const current = readKey();
    if (current === undefined) {
      entry.set(NOTHING);
      return;
    }
    const stop = untracked(() => reads.cache.watch<T>(current, (e) => entry.set(e)));
    onCleanup(stop);
  });

  const resource = resourceFromSnapshots<T | undefined>(() =>
    snapshotOf(entry(), readKey() !== undefined),
  );
  return Object.assign(resource, {
    fetchedAt: computed(() => entry().fetchedAt),
    stale: computed(() => entry().stale),
    refreshError: computed(() => (entry().hasValue ? entry().error : undefined)),
    reload: () => {
      const current = untracked(readKey);
      if (current !== undefined) void reads.cache.refresh(current);
    },
  });
}

function snapshotOf<T>(entry: ReadEntry<T>, asked: boolean): ResourceSnapshot<T | undefined> {
  if (entry.hasValue) {
    return { status: entry.fetching ? 'reloading' : 'resolved', value: entry.value };
  }
  if (entry.error !== undefined && !entry.fetching) {
    return {
      status: 'error',
      error: entry.error instanceof Error ? entry.error : new Error(String(entry.error)),
    };
  }
  return { status: asked ? 'loading' : 'idle', value: undefined };
}
