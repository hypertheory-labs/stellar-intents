import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideIntentStorage } from './durability';
import { INTENT_STORAGE, IntentStorage, memoryIntentStorage } from './intent-storage';
import { OutboxStore } from './outbox-store';
import { provideIntentTypes } from './registry';
import { defineIntentType, Intent, IntentState } from './types';

interface Hold {
  id: string;
  status: 'requested' | 'placed' | 'rejected';
  reason: string | null;
}

const placeHold = defineIntentType<{ title: string }, never, Hold>({
  name: 'place-hold',
  request: (intent) => ({ method: 'PUT', url: `/holds/${intent.id}`, body: intent.payload }),
  receipt: ({ payload }) => `We've received your request to place a hold on ${payload.title}.`,
  apology: { say: () => 'Sorry.', actions: [] },
  status: {
    url: (intent) => `/holds/${intent.id}`,
    outcome: (hold) => (hold.status === 'placed' ? { outcome: 'accepted' } : undefined),
  },
});

function setup(storage: IntentStorage = memoryIntentStorage()) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideIntentTypes(placeHold),
      { provide: INTENT_STORAGE, useValue: storage },
    ],
  });
  return {
    storage,
    store: TestBed.inject(OutboxStore),
    http: TestBed.inject(HttpTestingController),
  };
}

/** Let the storage's promises settle. */
const settled = () => new Promise((resolve) => setTimeout(resolve));

function stored(id: string, state: IntentState): Intent {
  return { id, type: 'place-hold', payload: { title: 'Dune' }, requestedAt: 1, state, schema: 2 };
}

describe('durability (PROTOCOL: Durability, proposed)', () => {
  it('stores an intent before sending it', async () => {
    let written!: () => void;
    const storage = memoryIntentStorage();
    const { store, http } = setup({
      ...storage,
      save: (owner, intent) =>
        intent.state.status === 'requested'
          ? new Promise((resolve) => (written = () => storage.save(owner, intent).then(resolve)))
          : storage.save(owner, intent),
    });
    await store.restore('P1');

    const intent = store.enqueue(placeHold, { title: 'Dune' });
    http.expectNone(`/holds/${intent.id}`);

    written();
    await settled();
    http.expectOne(`/holds/${intent.id}`);
    expect((await storage.load('P1')).map((i) => i.id)).toEqual([intent.id]);
  });

  it("stamps each intent with whose it is, and loads only that user's", async () => {
    const { store, storage, http } = setup();
    await store.restore('P1');

    const intent = store.enqueue(placeHold, { title: 'Dune' });
    await settled();
    http.expectOne(`/holds/${intent.id}`);

    expect((await storage.load('P1')).map((i) => i.id)).toEqual([intent.id]);
    expect(await storage.load('P2')).toEqual([]);
  });

  it("doesn't store anything until it knows whose it is, and sends at once", async () => {
    const { store, storage, http } = setup();

    const intent = store.enqueue(placeHold, { title: 'Dune' });
    http.expectOne(`/holds/${intent.id}`);
    await settled();

    expect(await storage.load('P1')).toEqual([]);
  });

  it('keeps the stored state current, and forgets an intent once retired', async () => {
    const { store, storage, http } = setup();
    await store.restore('P1');
    const intent = store.enqueue(placeHold, { title: 'Dune' });
    await settled();

    http.expectOne(`/holds/${intent.id}`).flush(null, { status: 202, statusText: 'Accepted' });
    await settled();
    expect((await storage.load('P1'))[0].state.status).toBe('received');

    store.retire(intent.id);
    await settled();
    expect(await storage.load('P1')).toEqual([]);
  });

  // conformance 19: sent, the page went away, the server had it -> outcome, no resend
  it('restores a sent intent as unknown, asks about it, and never resends it', async () => {
    const storage = memoryIntentStorage();
    await storage.save('P1', stored('a', { status: 'sent', sentAt: 5 }));
    const { store, http } = setup(storage);

    await store.restore('P1');
    expect(store.entityMap()['a'].state).toEqual({ status: 'unknown', sentAt: 5 });

    const check = http.expectOne(`/holds/a`);
    expect(check.request.method).toBe('GET');
    check.flush({ id: 'a', status: 'placed', reason: null });
    expect(store.entityMap()['a'].state).toMatchObject({ status: 'decided', outcome: 'accepted' });
    http.verify(); // no PUT
  });

  // the server never got it. Resending on a 404 isn't built (tmp/protocol-proposed.md);
  // until then it stays unknown, and its apology says so.
  it('leaves a restored intent unknown when the server never got it', async () => {
    const storage = memoryIntentStorage();
    await storage.save('P1', stored('a', { status: 'sent', sentAt: 5 }));
    const { store, http } = setup(storage);

    await store.restore('P1');
    http.expectOne('/holds/a').flush(null, { status: 404, statusText: 'Not Found' });

    expect(store.entityMap()['a'].state.status).toBe('unknown');
    expect(store.apologies().map((a) => a.intentId)).toEqual(['a']);
    http.verify();
  });

  it('restores a decided intent as it was, without asking', async () => {
    const storage = memoryIntentStorage();
    await storage.save('P1', stored('a', { status: 'decided', decidedAt: 5, outcome: 'accepted' }));
    const { store, http } = setup(storage);

    await store.restore('P1');
    expect(store.receipts().map((r) => [r.intentId, r.status])).toEqual([['a', 'decided']]);
    http.verify();
  });

  it("skips a stored intent whose type this build doesn't register", async () => {
    const storage = memoryIntentStorage();
    await storage.save('P1', {
      ...stored('a', { status: 'decided', decidedAt: 5, outcome: 'accepted' }),
      type: 'gone',
    });
    const { store } = setup(storage);

    await store.restore('P1');
    expect(store.entities()).toEqual([]);
  });

  it('upcasts an intent stored by an older build (schema 1: done, needs-attention)', async () => {
    const storage = memoryIntentStorage();
    const old = (id: string, state: unknown) =>
      ({ ...stored(id, { status: 'requested' }), state, schema: 1 }) as unknown as Intent;
    await storage.save('P1', old('a', { status: 'done', doneAt: 5 }));
    await storage.save('P1', old('b', { status: 'needs-attention', failure: { remedy: 'no' } }));
    const { store, http } = setup(storage);

    await store.restore('P1');
    expect(store.entityMap()['a']).toMatchObject({
      schema: 2,
      state: { status: 'decided', decidedAt: 5, outcome: 'accepted' },
    });
    expect(store.entityMap()['b'].state).toMatchObject({
      status: 'decided',
      outcome: 'declined',
      failure: { remedy: 'no' },
    });
    http.verify();
  });
});

describe('restored interrupted intents', () => {
  it('are asked about, but never resent without the person', async () => {
    const storage = memoryIntentStorage();
    const yesterday = {
      ...stored('a', { status: 'interrupted', failure: { remedy: 'try-again' } }),
    };
    await storage.save('P1', yesterday);
    const { store, http } = setup(storage);

    await store.restore('P1');
    http.expectOne('/holds/a').flush(null, { status: 404, statusText: 'Not Found' });

    store.resendInterrupted(); // what the stream does on subscribed
    http.expectNone('/holds/a');

    store.resend('a'); // the person said "try again"
    expect(http.expectOne('/holds/a').request.method).toBe('PUT');
  });
});

describe('provideIntentStorage', () => {
  function boot(storage: IntentStorage) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideIntentTypes(placeHold),
        provideIntentStorage({
          sessionUrl: '/session',
          userOf: (session: { patronId: string | null }) => session.patronId ?? undefined,
          storage,
        }),
      ],
    });
    return { store: TestBed.inject(OutboxStore), http: TestBed.inject(HttpTestingController) };
  }

  // conformance 20: Pat's stored intent, Sam signs in -> nothing sent or checked for Pat
  it("asks the session who's signed in, and restores only their intents", async () => {
    const storage = memoryIntentStorage();
    await storage.save('PAT', stored('pats', { status: 'sent', sentAt: 5 }));
    await storage.save('SAM', stored('sams', { status: 'sent', sentAt: 5 }));
    const { store, http } = boot(storage);

    http.expectOne('/session').flush({ isAuthenticated: true, patronId: 'SAM' });
    await settled();

    expect(store.entities().map((i) => i.id)).toEqual(['sams']);
    http.expectOne('/holds/sams');
    http.verify();
  });

  it('restores nothing while signed out', async () => {
    const storage = memoryIntentStorage();
    await storage.save('PAT', stored('pats', { status: 'sent', sentAt: 5 }));
    const { store, http } = boot(storage);

    http.expectOne('/session').flush({ isAuthenticated: false, patronId: null });
    await settled();

    expect(store.entities()).toEqual([]);
    http.verify();
  });
});
