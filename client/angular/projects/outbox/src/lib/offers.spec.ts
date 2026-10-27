import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OutboxStore } from './outbox-store';
import { provideIntentTypes } from './registry';
import { defineIntentType, OfferGroup } from './types';

// PROTOCOL §1, Offers: a decline says what can be chosen instead, and the client sends one
// back as it came, to the offers' id.

const placeHold = defineIntentType<{ titleId: number }>({
  name: 'place-hold',
  request: ({ id, payload }) => ({
    method: 'PUT',
    url: `/errands/${id}`,
    body: { type: 'place-hold', ...payload },
  }),
  receipt: () => 'Received.',
  apology: { say: (_, failure) => failure.detail ?? 'No.', actions: [] },
});

interface SwapHold {
  type: 'swap-hold';
  giveUp: string;
  titleId: number;
}

const swapHold = defineIntentType<SwapHold>({
  name: 'swap-hold',
  // an offer is already the whole errand
  request: ({ id, payload }) => ({ method: 'PUT', url: `/errands/${id}`, body: payload }),
  receipt: () => 'Received.',
  apology: { say: () => 'No.', actions: [] },
});

const swaps: OfferGroup = {
  id: 'o1',
  choose: [
    { type: 'swap-hold', giveUp: 'h1', titleId: 1010 },
    { type: 'swap-hold', giveUp: 'h2', titleId: 1010 },
  ],
};

function declinedWithOffers() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideIntentTypes(placeHold, swapHold),
    ],
  });
  const store = TestBed.inject(OutboxStore);
  const http = TestBed.inject(HttpTestingController);
  const declined = store.enqueue(placeHold, { titleId: 1010 });
  http.expectOne(`/errands/${declined.id}`).flush(null, { status: 202, statusText: 'Accepted' });
  store.settle(declined.id, {
    outcome: 'declined',
    detail: 'You already have 3 holds.',
    offers: [swaps],
  });
  return { store, http, declined };
}

const choice = (n: number) => swaps.choose[n] as SwapHold;

describe('offers', () => {
  it('a decline keeps its offers, and its apology shows them', () => {
    const { store, declined } = declinedWithOffers();

    const apology = store.apologies().find((a) => a.intentId === declined.id)!;
    expect(apology.message).toBe('You already have 3 holds.');
    expect(apology.offers).toEqual([swaps]);
  });

  it('taking one sends it exactly as it came, to the offers id, and lets the decline go', () => {
    const { store, http, declined } = declinedWithOffers();

    const taken = store.takeOffer(swapHold, declined.id, swaps, choice(1));

    expect(taken.id).toBe('o1');
    const req = http.expectOne('/errands/o1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ type: 'swap-hold', giveUp: 'h2', titleId: 1010 });
    expect(req.request.headers.get('Idempotency-Key')).toBe('o1');
    expect(store.entityMap()[declined.id]).toBeUndefined();
    expect(store.apologies()).toEqual([]);
  });

  it('taking a second one on this page is the first one: nothing more is sent', () => {
    const { store, http, declined } = declinedWithOffers();
    store.takeOffer(swapHold, declined.id, swaps, choice(0));
    http.expectOne('/errands/o1');

    const again = store.takeOffer(swapHold, declined.id, swaps, choice(1));

    expect(again.payload).toEqual(choice(0));
    http.expectNone('/errands/o1');
  });

  it('only what was offered can be taken, and only as its own type', () => {
    const { store, declined } = declinedWithOffers();

    // built by the client, even if equal: the client never builds an offer
    expect(() =>
      store.takeOffer(swapHold, declined.id, swaps, {
        type: 'swap-hold',
        giveUp: 'h1',
        titleId: 1010,
      }),
    ).toThrow();
    expect(() => store.takeOffer(placeHold, declined.id, swaps, choice(0) as never)).toThrow();
  });

  it('a decline without offers shows none', () => {
    const { store, declined } = declinedWithOffers();
    const plain = store.enqueue(placeHold, { titleId: 1001 });
    TestBed.inject(HttpTestingController)
      .expectOne(`/errands/${plain.id}`)
      .flush(null, { status: 202, statusText: 'Accepted' });
    store.settle(plain.id, { outcome: 'declined', detail: 'No.' });

    expect(store.apologies().find((a) => a.intentId === plain.id)!.offers).toEqual([]);
    expect(store.apologies().find((a) => a.intentId === declined.id)!.offers).toHaveLength(1);
  });
});
