import { defineIntentType } from 'outbox';

/** A hold as the server represents it (GET /holds/{id}). */
export interface Hold {
  id: string;
  version: number;
  patronId: string;
  titleId: number;
  branchCode: string;
  status: 'requested' | 'placed' | 'rejected' | 'cancelled';
  reason: string | null;
  requestedAt: string;
  decidedAt: string | null;
}

/** The seeded titles (server/dotnet/ReferenceApi/Holds/SeedHolds.cs). There's no titles API. */
export const TITLES = [
  { id: 1001, name: 'Dune' },
  { id: 1005, name: 'Kindred' },
  { id: 1006, name: 'Parable of the Sower' },
  { id: 1010, name: 'Beloved' },
] as const;

export const titleName = (id: number) => TITLES.find((t) => t.id === id)?.name ?? `title ${id}`;

/** Which reads a change to a hold makes stale. The same keys the server sends (HoldKeys.cs). */
const holdKeys = (holdId: string, patronId: string, titleId: number) => [
  `/holds/${holdId}`,
  `/holds?patronId=${patronId}`,
  `/holds?titleId=${titleId}`,
];

export interface PlaceHoldPayload {
  patronId: string;
  titleId: number;
  branchCode: string;
}

/** Placing a hold: a create. The intent's id is the hold's id. */
export const placeHold = defineIntentType<PlaceHoldPayload, Hold, Hold>({
  name: 'place-hold',
  // patronId is in the payload for the read keys only. The server takes it from the session.
  request: ({ id, payload: { titleId, branchCode } }) => ({
    method: 'PUT',
    url: `/api/holds/${id}`,
    body: { titleId, branchCode },
  }),
  receipt: ({ payload }) =>
    `We've received your request to place a hold on ${titleName(payload.titleId)}.`,
  candidate: ({ id, payload, requestedAt }) => ({
    id,
    version: 0,
    ...payload,
    status: 'requested',
    reason: null,
    requestedAt: new Date(requestedAt).toISOString(),
    decidedAt: null,
  }),
  apology: {
    say: ({ payload }, failure) => {
      const title = titleName(payload.titleId);
      switch (failure.remedy) {
        case 'wait':
          return `We're not sure your request for ${title} went through. Check your holds before trying again.`;
        case 'try-again':
          return `We couldn't get your request for ${title} to the library. Please try again.`;
        case 'sign-in':
          return `Please sign in again to request ${title}.`;
        case 'no':
          return `We couldn't place your hold on ${title}: ${failure.detail ?? 'the library said no.'}`;
      }
    },
    actions: ['retry'],
  },
  timeoutMs: 10_000,
  status: {
    url: ({ id }) => `/api/holds/${id}`,
    outcome: (hold) => {
      switch (hold.status) {
        case 'placed':
          return { outcome: 'accepted' };
        case 'rejected':
          return { outcome: 'declined', detail: hold.reason ?? undefined };
        case 'cancelled': // cancelled before it was decided; the server says the same
          return { outcome: 'withdrawn' };
        case 'requested':
          return undefined;
      }
    },
  },
  invalidates: ({ id, payload }) => holdKeys(id, payload.patronId, payload.titleId),
});

export interface CancelHoldPayload {
  holdId: string;
  patronId: string;
  titleId: number;
}

/**
 * Cancelling a hold: not a create, so the intent's id is NOT the hold's id, and "has
 * memory got it?" doesn't apply. Its promotion is the opposite: done, and the hold is
 * gone from memory (see App).
 */
export const cancelHold = defineIntentType<CancelHoldPayload, never, Hold>({
  name: 'cancel-hold',
  request: ({ payload }) => ({ method: 'DELETE', url: `/api/holds/${payload.holdId}` }),
  receipt: ({ payload }) =>
    `We've received your request to cancel your hold on ${titleName(payload.titleId)}.`,
  apology: {
    say: ({ payload }, failure) =>
      `We couldn't cancel your hold on ${titleName(payload.titleId)}${failure.detail ? `: ${failure.detail}` : '.'}`,
    actions: [],
  },
  // The read that confirms a cancel: the hold itself says "cancelled". Without this, a
  // cancel whose answer was lost stayed unknown forever (unsettled/holds-in-irmto.md).
  status: {
    url: ({ payload }) => `/api/holds/${payload.holdId}`,
    outcome: (hold) =>
      hold.status === 'cancelled'
        ? { outcome: 'accepted' }
        : hold.status === 'rejected'
          ? {
              outcome: 'declined',
              detail: "This hold was never placed, so there's nothing to cancel.",
            }
          : undefined,
  },
  invalidates: ({ payload }) => holdKeys(payload.holdId, payload.patronId, payload.titleId),
});
