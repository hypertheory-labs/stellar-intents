import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OutboxStore } from './outbox-store';
import { provideIntentTypes } from './registry';
import { defineIntentType } from './types';

/** The server's representation, as GET /holds/{id} answers it. */
interface Hold {
  id: string;
  status: 'requested' | 'placed' | 'rejected';
  reason: string | null;
}

const placeHold = defineIntentType<{ title: string }, never, Hold>({
  name: 'place-hold',
  request: (intent) => ({ method: 'PUT', url: `/holds/${intent.id}`, body: intent.payload }),
  receipt: ({ payload }) => `We've received your request to place a hold on ${payload.title}.`,
  apology: {
    say: ({ payload }, failure) =>
      `We couldn't place a hold on ${payload.title}: ${failure.detail ?? 'no reason given'}.`,
    actions: ['retry'],
  },
  timeoutMs: 5_000,
  status: {
    url: (intent) => `/holds/${intent.id}`,
    outcome: (hold) =>
      hold.status === 'placed'
        ? { outcome: 'accepted' }
        : hold.status === 'rejected'
          ? { outcome: 'declined', detail: hold.reason ?? undefined }
          : undefined,
  },
});

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideIntentTypes(placeHold)],
  });
  return { store: TestBed.inject(OutboxStore), http: TestBed.inject(HttpTestingController) };
}

/** An intent the server accepted (202): its outcome comes later. */
function received({ store, http }: ReturnType<typeof setup>) {
  const intent = store.enqueue(placeHold, { title: 'Dune' });
  http.expectOne(`/holds/${intent.id}`).flush(null, { status: 202, statusText: 'Accepted' });
  return intent;
}

/** The state, or for a decided intent, how it ended. */
const statusOf = (store: ReturnType<typeof setup>['store'], id: string) => {
  const state = store.entityMap()[id]?.state;
  return state?.status === 'decided' ? state.outcome : state?.status;
};

describe('status checks (PROTOCOL §2)', () => {
  afterEach(() => vi.useRealTimers());

  it('asks where the type says, and a placed hold is done', () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.store.checkStatus(intent.id);
    ctx.http
      .expectOne(`/holds/${intent.id}`)
      .flush({ id: intent.id, status: 'placed', reason: null });

    expect(statusOf(ctx.store, intent.id)).toBe('accepted');
  });

  it("a rejected hold needs attention, and the apology can say the server's reason", () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.store.checkStatus(intent.id);
    ctx.http
      .expectOne(`/holds/${intent.id}`)
      .flush({ id: intent.id, status: 'rejected', reason: 'You already have 3 holds, the limit' });

    expect(statusOf(ctx.store, intent.id)).toBe('declined');
    expect(ctx.store.apologies()[0].message).toBe(
      "We couldn't place a hold on Dune: You already have 3 holds, the limit.",
    );
  });

  it('still requested: nothing changes, the receipt is still true', () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.store.checkStatus(intent.id);
    ctx.http
      .expectOne(`/holds/${intent.id}`)
      .flush({ id: intent.id, status: 'requested', reason: null });

    expect(statusOf(ctx.store, intent.id)).toBe('received');
  });

  it('a 404 is "not recorded yet", not an outcome', () => {
    const ctx = setup();
    const intent = received(ctx);

    ctx.store.checkStatus(intent.id);
    ctx.http.expectOne(`/holds/${intent.id}`).flush(null, { status: 404, statusText: 'Not Found' });

    expect(statusOf(ctx.store, intent.id)).toBe('received');
  });

  it('checkWaiting asks about received and unknown intents, and nothing else', () => {
    vi.useFakeTimers();
    const ctx = setup();
    const accepted = received(ctx);
    const timedOut = ctx.store.enqueue(placeHold, { title: 'Kindred' });
    const timedOutPut = ctx.http.expectOne(`/holds/${timedOut.id}`);
    vi.advanceTimersByTime(5_000); // unknown
    const stillSending = ctx.store.enqueue(placeHold, { title: 'Beloved' });
    const stillSendingPut = ctx.http.expectOne(`/holds/${stillSending.id}`);

    ctx.store.checkWaiting();

    const asked = ctx.http
      .match((r) => r.method === 'GET')
      .map((r) => r.request.url)
      .sort();
    expect(asked).toEqual([`/holds/${accepted.id}`, `/holds/${timedOut.id}`].sort());
    timedOutPut.flush(null, { status: 202, statusText: 'Accepted' });
    stillSendingPut.flush(null, { status: 202, statusText: 'Accepted' });
  });

  it('conformance 9: once declined, a later "accepted" from a status check changes nothing', () => {
    const ctx = setup();
    const intent = received(ctx);
    ctx.store.settle(intent.id, { outcome: 'declined', detail: 'No.' });

    ctx.store.checkStatus(intent.id);
    ctx.http
      .expectOne(`/holds/${intent.id}`)
      .flush({ id: intent.id, status: 'placed', reason: null });

    expect(statusOf(ctx.store, intent.id)).toBe('declined');
  });

  it('conformance 10: a decision about an intent this client never held is ignored', () => {
    const { store } = setup();
    expect(() => store.settle('not-ours', { outcome: 'accepted' })).not.toThrow();
    expect(store.entities()).toEqual([]);
  });
});
