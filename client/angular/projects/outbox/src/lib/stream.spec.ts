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
    outcome: (hold) => (hold.status === 'placed' ? { status: 'done' } : undefined),
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

const statusOf = (store: OutboxStore, id: string) => store.entityMap()[id]?.state.status;
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
    expect(statusOf(ctx.store, intent.id)).toBe('done');
    expect(ctx.seen.map((e) => e.type)).toEqual(['[Server] subscribed']);
  });

  it('intent-decided settles the intent, with the detail for the apology', () => {
    const ctx = setup();
    const placed = received(ctx);
    const refused = received(ctx);

    ctx.latest().emit('intent-decided', { intentId: placed.id, outcome: 'done' });
    ctx.latest().emit('intent-decided', {
      intentId: refused.id,
      outcome: 'needs-attention',
      detail: 'You already have 3 holds, the limit.',
    });

    expect(statusOf(ctx.store, placed.id)).toBe('done');
    expect(statusOf(ctx.store, refused.id)).toBe('needs-attention');
    expect(ctx.store.apologies()[0].message).toBe('You already have 3 holds, the limit.');
  });

  it('conformance 8: intent-decided, then the late 202 of the command: still done', () => {
    const ctx = setup();
    const intent = ctx.store.enqueue(placeHold, { title: 'Dune' });
    const put = ctx.http.expectOne(`/holds/${intent.id}`);

    ctx.latest().emit('intent-decided', { intentId: intent.id, outcome: 'done' });
    put.flush(null, { status: 202, statusText: 'Accepted' });

    expect(statusOf(ctx.store, intent.id)).toBe('done');
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
    expect(ctx.seen.map((e) => e.type)).toEqual(['[Server] disconnected', '[Server] signedOut']);
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

describe('the floor: checking when the user comes back (PROTOCOL §2)', () => {
  it('conformance 13: with no stream at all, coming back to the tab still reaches the outcome', () => {
    const ctx = setup({ stream: false });
    const intent = received(ctx);

    const document = TestBed.inject(DOCUMENT);
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));

    ctx.http.expectOne(`/holds/${intent.id}`).flush({ status: 'placed' });
    expect(statusOf(ctx.store, intent.id)).toBe('done');
    expect(ctx.sources).toEqual([]);
  });
});
