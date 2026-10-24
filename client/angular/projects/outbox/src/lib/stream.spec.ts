import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Events } from '@ngrx/signals/events';
import { invalidationEvents } from './invalidation';
import { OutboxStore } from './outbox-store';
import { provideIntentTypes } from './registry';
import {
  EVENT_SOURCE,
  EventSourceLike,
  provideOutboxStatusChecks,
  provideOutboxStream,
  serverEvents,
} from './stream';
import { defineIntentType } from './types';

/** An EventSource the test drives: `emit` an event, `close` it the way a browser gives up. */
class FakeSource implements EventSourceLike {
  readyState = 1;
  private listeners = new Map<string, ((e: MessageEvent<string>) => void)[]>();
  constructor(readonly url: string) {}
  addEventListener(type: string, listener: (e: MessageEvent<string>) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  close() {
    this.readyState = 2;
  }
  emit(type: string, data: unknown = {}) {
    this.emitRaw(type, JSON.stringify(data));
  }
  emitRaw(type: string, data: string) {
    const event = new MessageEvent<string>(type, { data });
    this.listeners.get(type)?.forEach((l) => l(event));
  }
  /** The browser gave up on it: a 401, or the server went away. */
  giveUp() {
    this.readyState = 2;
    this.listeners.get('error')?.forEach((l) => l(new MessageEvent('error')));
  }
}

const placeHold = defineIntentType<{ title: string }, never, { status: string }>({
  name: 'place-hold',
  request: (intent) => ({ method: 'PUT', url: `/holds/${intent.id}`, body: intent.payload }),
  receipt: ({ payload }) => `We've received your request to place a hold on ${payload.title}.`,
  apology: { say: (_, failure) => failure.detail ?? 'No.', actions: [] },
  status: {
    url: (intent) => `/holds/${intent.id}`,
    outcome: (hold) => (hold.status === 'placed' ? { outcome: 'accepted' } : undefined),
  },
});

function setup({ stream = true } = {}) {
  const sources: FakeSource[] = [];
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideIntentTypes(placeHold),
      provideOutboxStatusChecks(),
      ...(stream ? [provideOutboxStream({ eventsUrl: '/events', sessionUrl: '/session' })] : []),
      {
        provide: EVENT_SOURCE,
        useValue: (url: string) => {
          const source = new FakeSource(url);
          sources.push(source);
          return source;
        },
      },
    ],
  });
  const store = TestBed.inject(OutboxStore);
  const http = TestBed.inject(HttpTestingController);
  const seen: { type: string; payload?: unknown }[] = [];
  TestBed.inject(Events)
    .on(...Object.values(serverEvents), invalidationEvents.invalidated)
    .subscribe((e) => seen.push(e));
  const latest = () => sources[sources.length - 1];
  return { store, http, sources, latest, seen };
}

/** An intent the server accepted (202): its outcome comes later. */
function received({ store, http }: Pick<ReturnType<typeof setup>, 'store' | 'http'>) {
  const intent = store.enqueue(placeHold, { title: 'Dune' });
  http.expectOne(`/holds/${intent.id}`).flush(null, { status: 202, statusText: 'Accepted' });
  return intent;
}

/** The state, or for a decided intent, how it ended. */
const statusOf = (store: OutboxStore, id: string) => {
  const state = store.entityMap()[id]?.state;
  return state?.status === 'decided' ? state.outcome : state?.status;
};
type OutboxStore = ReturnType<typeof setup>['store'];

describe('the event stream (PROTOCOL §3)', () => {
  afterEach(() => vi.useRealTimers());

  it('opens the stream when the app starts', () => {
    const { sources } = setup();
    expect(sources.map((s) => s.url)).toEqual(['/events']);
  });

  it('conformance 11: on subscribed, every waiting intent gets a status check, and it is announced', () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.latest().emit('subscribed');

    ctx.http.expectOne(`/holds/${intent.id}`).flush({ status: 'placed' });
    expect(statusOf(ctx.store, intent.id)).toBe('accepted');
    expect(ctx.seen.map((e) => e.type)).toEqual(['[Server] subscribed']);
  });

  it("on subscribed, this page's interrupted intents go again with the same id", () => {
    const ctx = setup();
    const intent = ctx.store.enqueue(placeHold, { title: 'Dune' });
    ctx.http.expectOne(`/holds/${intent.id}`).error(new ProgressEvent('error'));

    ctx.latest().emit('subscribed');

    const requests = ctx.http.match(`/holds/${intent.id}`);
    expect(requests.map((r) => r.request.method).sort()).toEqual(['GET', 'PUT']);
    requests
      .find((r) => r.request.method === 'PUT')!
      .flush(null, {
        status: 202,
        statusText: 'Accepted',
      });
    expect(statusOf(ctx.store, intent.id)).toBe('received');
  });

  it('intent-decided settles the intent, with the detail for the apology', () => {
    const ctx = setup();
    const placed = received(ctx);
    const refused = received(ctx);

    ctx.latest().emit('intent-decided', { intentId: placed.id, outcome: 'accepted' });
    ctx.latest().emit('intent-decided', {
      intentId: refused.id,
      outcome: 'declined',
      detail: 'You already have 3 holds, the limit.',
    });

    expect(statusOf(ctx.store, placed.id)).toBe('accepted');
    expect(statusOf(ctx.store, refused.id)).toBe('declined');
    expect(ctx.store.apologies()[0].message).toBe('You already have 3 holds, the limit.');
  });

  // unsettled/holds-in-irmto.md: a cancel that got there before the decision
  it('withdrawn lets the intent go: no apology, no guess, nothing left to show', () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.latest().emit('intent-decided', { intentId: intent.id, outcome: 'withdrawn' });

    expect(ctx.store.entityMap()[intent.id]).toBeUndefined();
    expect(ctx.store.apologies()).toEqual([]);
    expect(ctx.store.receipts()).toEqual([]);
  });

  it("ignores an outcome it doesn't know (a newer server)", () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.latest().emit('intent-decided', { intentId: intent.id, outcome: 'postponed' });

    expect(statusOf(ctx.store, intent.id)).toBe('received');
  });

  it('conformance 8: intent-decided, then the late 202 of the command: still done', () => {
    const ctx = setup();
    const intent = ctx.store.enqueue(placeHold, { title: 'Dune' });
    const put = ctx.http.expectOne(`/holds/${intent.id}`);

    ctx.latest().emit('intent-decided', { intentId: intent.id, outcome: 'accepted' });
    put.flush(null, { status: 202, statusText: 'Accepted' });

    expect(statusOf(ctx.store, intent.id)).toBe('accepted');
  });

  it('invalidate is announced with its keys, for whoever holds reads', () => {
    const ctx = setup();
    ctx.latest().emit('invalidate', { keys: ['/holds?patronId=P0042'] });
    expect(ctx.seen).toContainEqual(
      expect.objectContaining({
        type: '[Reads] invalidated',
        payload: { keys: ['/holds?patronId=P0042'], from: 'server' },
      }),
    );
  });

  it('a malformed event is ignored, not thrown', () => {
    const ctx = setup();
    expect(() => ctx.latest().emitRaw('intent-decided', 'not json')).not.toThrow();
  });

  it('conformance 12: dropped and still signed in, it reconnects with backoff', () => {
    vi.useFakeTimers();
    const ctx = setup();

    ctx.latest().giveUp();
    ctx.http.expectOne('/session').flush({ isAuthenticated: true });
    expect(ctx.sources.length).toBe(1);

    vi.advanceTimersByTime(1_000);
    expect(ctx.sources.length).toBe(2);

    ctx.latest().giveUp(); // again, before it ever said "subscribed"
    ctx.http.expectOne('/session').flush({ isAuthenticated: true });
    vi.advanceTimersByTime(1_999);
    expect(ctx.sources.length).toBe(2);
    vi.advanceTimersByTime(1);
    expect(ctx.sources.length).toBe(3);
  });

  it('dropped and signed out: it says so and stops reconnecting', () => {
    vi.useFakeTimers();
    const ctx = setup();

    ctx.latest().giveUp();
    ctx.http.expectOne('/session').flush({ isAuthenticated: false });
    vi.advanceTimersByTime(60_000);

    expect(ctx.sources.length).toBe(1);
    // it never said "subscribed", so there was no connection to lose
    expect(ctx.seen.map((e) => e.type)).toEqual(['[Server] signedOut']);
  });

  it('dropped and the session can’t be reached either: it keeps trying', () => {
    vi.useFakeTimers();
    const ctx = setup();

    ctx.latest().giveUp();
    ctx.http.expectOne('/session').error(new ProgressEvent('error'));
    vi.advanceTimersByTime(1_000);

    expect(ctx.sources.length).toBe(2);
  });
});

describe('silence (a proxy can hold a dead stream open)', () => {
  afterEach(() => vi.useRealTimers());

  it('no event, not even a heartbeat, for a minute: treated as dropped, and announced', () => {
    vi.useFakeTimers();
    const ctx = setup();
    ctx.latest().emit('subscribed');

    vi.advanceTimersByTime(30_000);
    ctx.latest().emit('heartbeat'); // still alive
    vi.advanceTimersByTime(45_000);
    ctx.http.expectNone('/session');

    vi.advanceTimersByTime(20_000); // 65 s since the last heartbeat
    expect(ctx.sources[0].readyState).toBe(2); // closed by us
    expect(ctx.seen.map((e) => e.type)).toContain('[Server] disconnected');
    ctx.http.expectOne('/session').flush({ isAuthenticated: true });

    vi.advanceTimersByTime(1_000);
    expect(ctx.sources.length).toBe(2); // reconnecting
  });

  it('a stream that drops once is handled once (silence, then the late error)', () => {
    vi.useFakeTimers();
    const ctx = setup();
    ctx.latest().emit('subscribed');
    vi.advanceTimersByTime(65_000);
    ctx.http.expectOne('/session');

    ctx.sources[0].giveUp(); // the browser finally notices too
    ctx.http.expectNone('/session');
  });
});

describe('the floor: checking when the user comes back (PROTOCOL §2)', () => {
  it('conformance 13: with no stream at all, coming back to the tab still reaches the outcome', () => {
    const ctx = setup({ stream: false });
    const intent = received(ctx);

    const document = TestBed.inject(DOCUMENT);
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));

    ctx.http.expectOne(`/holds/${intent.id}`).flush({ status: 'placed' });
    expect(statusOf(ctx.store, intent.id)).toBe('accepted');
    expect(ctx.sources).toEqual([]);
  });
});
