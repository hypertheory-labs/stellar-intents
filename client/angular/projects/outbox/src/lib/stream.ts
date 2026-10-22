import { HttpClient } from '@angular/common/http';
import {
  DestroyRef,
  DOCUMENT,
  EnvironmentProviders,
  inject,
  InjectionToken,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import { type } from '@ngrx/signals';
import { eventGroup, injectDispatch } from '@ngrx/signals/events';
import { OutboxStore } from './outbox-store';

/*
  How the outbox hears about outcomes without being asked, and the floor under it.

  PROTOCOL's ladder: a status check is the floor (provideOutboxStatusChecks: ask when the
  user comes back to the tab), and the event stream is an enhancement on top
  (provideOutboxStream). A client that never opens the stream still reaches every outcome.
*/

/**
 * What the stream says that isn't about the outbox's own intents, published for whoever
 * cares (a read cache, mostly). The outbox never knows who's listening.
 */
export const serverEvents = eventGroup({
  source: 'Server',
  events: {
    /** (Re)connected. Anything that happened while away isn't coming: refetch what's on screen. */
    subscribed: type<void>(),
    /** Reads under these keys changed (PROTOCOL §3, invalidate). */
    invalidated: type<{ keys: string[] }>(),
    /** The stream dropped and the session says signed out. The outbox stops reconnecting. */
    signedOut: type<void>(),
  },
});

export interface OutboxStreamOptions {
  /** The event stream (PROTOCOL §3), e.g. `/api/events`. */
  readonly eventsUrl: string;
  /** The session resource (PROTOCOL §0), e.g. `/api/session`. */
  readonly sessionUrl: string;
  /** Only for a cross-origin API. Same-origin (an edge, a dev proxy) sends the cookie anyway. */
  readonly withCredentials?: boolean;
}

/** The parts of EventSource the stream uses, so a test can supply a fake. */
export interface EventSourceLike {
  readonly readyState: number;
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void;
  close(): void;
}

/** Makes the EventSource. Replace it in tests. */
export const EVENT_SOURCE = new InjectionToken<
  (url: string, init: EventSourceInit) => EventSourceLike
>('outbox EventSource factory', {
  factory: () => (url, init) => new EventSource(url, init) as EventSourceLike,
});

/** EventSource.CLOSED, without needing EventSource to exist (it doesn't, in tests). */
const CLOSED = 2;
/** Backoff between reconnects: 1 s, 2 s, 4 s, … up to this. */
const MAX_RECONNECT_MS = 30_000;

/**
 * Opens the event stream for the app's lifetime (PROTOCOL §3), and:
 * - on `subscribed` (every connect), asks about every waiting intent, and announces it;
 * - on `intent-decided`, settles the intent;
 * - on `invalidate`, announces the keys;
 * - when it drops, asks the session whether we're still signed in, then reconnects with
 *   backoff, or stops.
 */
export function provideOutboxStream(options: OutboxStreamOptions): EnvironmentProviders {
  return makeEnvironmentProviders([provideEnvironmentInitializer(() => follow(options))]);
}

/**
 * The floor: ask about every waiting intent when the user comes back to the tab
 * (PROTOCOL §2). Works with or without the stream.
 */
export function provideOutboxStatusChecks(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideEnvironmentInitializer(() => {
      const document = inject(DOCUMENT);
      const outbox = inject(OutboxStore);
      const onVisible = () => {
        if (document.visibilityState === 'visible') outbox.checkWaiting();
      };
      document.addEventListener('visibilitychange', onVisible);
      inject(DestroyRef).onDestroy(() =>
        document.removeEventListener('visibilitychange', onVisible),
      );
    }),
  ]);
}

function follow({ eventsUrl, sessionUrl, withCredentials = false }: OutboxStreamOptions) {
  const outbox = inject(OutboxStore);
  const http = inject(HttpClient);
  const create = inject(EVENT_SOURCE);
  const dispatch = injectDispatch(serverEvents);

  let source: EventSourceLike | undefined;
  let attempt = 0;
  let reconnect: ReturnType<typeof setTimeout> | undefined;
  let stopped = false;

  function open() {
    if (stopped) return;
    const opened = create(eventsUrl, { withCredentials });
    source = opened;

    opened.addEventListener('subscribed', () => {
      attempt = 0;
      outbox.checkWaiting();
      dispatch.subscribed();
    });
    opened.addEventListener('intent-decided', (event) => {
      const decided = parse<{ intentId: string; outcome: string; detail?: string | null }>(event);
      if (!decided) return;
      outbox.settle(
        decided.intentId,
        decided.outcome === 'done'
          ? { status: 'done' }
          : { status: 'needs-attention', detail: decided.detail ?? undefined },
      );
    });
    opened.addEventListener('invalidate', (event) => {
      const invalidate = parse<{ keys: string[] }>(event);
      if (invalidate) dispatch.invalidated({ keys: invalidate.keys });
    });
    // EventSource retries some failures by itself (readyState back to CONNECTING). The ones
    // it gives up on (CLOSED) include a 401 and, in Chrome, an API restart: those are ours.
    opened.addEventListener('error', () => {
      if (opened.readyState === CLOSED) dropped(opened);
    });
  }

  // WHY ask the session before reconnecting: EventSource can't say why it closed. Signed out
  // and the server going away look the same, and reconnecting to a 401 forever is a loop.
  function dropped(closed: EventSourceLike) {
    closed.close();
    if (stopped) return;
    http.get<{ isAuthenticated: boolean }>(sessionUrl).subscribe({
      next: (session) => {
        if (session.isAuthenticated) return later();
        stopped = true;
        dispatch.signedOut();
      },
      error: () => later(), // the server isn't answering at all: try again later
    });
  }

  function later() {
    const delay = Math.min(MAX_RECONNECT_MS, 1_000 * 2 ** attempt++);
    reconnect = setTimeout(open, delay);
  }

  inject(DestroyRef).onDestroy(() => {
    stopped = true;
    clearTimeout(reconnect);
    source?.close();
  });

  open();
}

/** An event's data, or undefined if it isn't the JSON we expect. A bad event is ignored. */
function parse<T>(event: MessageEvent<string>): T | undefined {
  try {
    return JSON.parse(event.data) as T;
  } catch {
    return undefined;
  }
}
