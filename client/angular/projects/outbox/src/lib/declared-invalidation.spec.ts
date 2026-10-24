import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Events } from '@ngrx/signals/events';
import { invalidationEvents } from './invalidation';
import { OutboxStore } from './outbox-store';
import { provideIntentTypes } from './registry';
import { defineIntentType } from './types';

const placeHold = defineIntentType<{ patronId: string; titleId: number }>({
  name: 'place-hold',
  request: (intent) => ({ method: 'PUT', url: `/holds/${intent.id}`, body: intent.payload }),
  receipt: () => 'Received.',
  apology: { say: () => 'Sorry.', actions: [] },
  // the same keys the server sends (server/dotnet/ReferenceApi/Holds/HoldKeys.cs)
  invalidates: ({ id, payload }) => [
    `/holds/${id}`,
    `/holds?patronId=${payload.patronId}`,
    `/holds?titleId=${payload.titleId}`,
  ],
});

const undeclared = defineIntentType<{ titleId: number }>({
  name: 'undeclared',
  request: (intent) => ({ method: 'PUT', url: `/other/${intent.id}`, body: intent.payload }),
  receipt: () => 'Received.',
  apology: { say: () => 'Sorry.', actions: [] },
});

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideIntentTypes(placeHold, undeclared),
    ],
  });
  const announced: { keys: readonly string[]; from: string }[] = [];
  TestBed.inject(Events)
    .on(invalidationEvents.invalidated)
    .subscribe((e) => announced.push(e.payload));
  return {
    store: TestBed.inject(OutboxStore),
    http: TestBed.inject(HttpTestingController),
    announced,
  };
}

const payload = { patronId: 'P0042', titleId: 1001 };

describe('declared invalidation (the client half of the symmetry)', () => {
  it('a 202 is announced with the declared keys: the server recorded something', () => {
    const { store, http, announced } = setup();
    const intent = store.enqueue(placeHold, payload);
    http.expectOne(`/holds/${intent.id}`).flush(null, { status: 202, statusText: 'Accepted' });

    expect(announced).toEqual([
      {
        keys: [`/holds/${intent.id}`, '/holds?patronId=P0042', '/holds?titleId=1001'],
        from: 'intent',
      },
    ]);
  });

  it('and again when it is decided later, either way', () => {
    const { store, http, announced } = setup();
    const placed = store.enqueue(placeHold, payload);
    http.expectOne(`/holds/${placed.id}`).flush(null, { status: 202, statusText: 'Accepted' });
    const refused = store.enqueue(placeHold, payload);
    http.expectOne(`/holds/${refused.id}`).flush(null, { status: 202, statusText: 'Accepted' });

    store.settle(placed.id, { outcome: 'accepted' });
    store.settle(refused.id, { outcome: 'declined', detail: 'No.' });

    expect(announced.length).toBe(4);
  });

  it('a rejection straight from the command changed nothing, so nothing is announced', () => {
    const { store, http, announced } = setup();
    const intent = store.enqueue(placeHold, payload);
    http.expectOne(`/holds/${intent.id}`).flush(null, { status: 409, statusText: 'Conflict' });

    expect(announced).toEqual([]);
  });

  it('a type that declares nothing announces nothing', () => {
    const { store, http, announced } = setup();
    const intent = store.enqueue(undeclared, { titleId: 1 });
    http.expectOne(`/other/${intent.id}`).flush(null, { status: 202, statusText: 'Accepted' });

    expect(announced).toEqual([]);
  });
});
