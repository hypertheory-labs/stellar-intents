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
const holdKeys = (patronId: string, titleId: number) => [
  `/holds?patronId=${patronId}`,
  `/holds?titleId=${titleId}`,
];

/** An errand as the server represents it (GET /errands/{id}, PROTOCOL §5). */
interface Errand {
  id: string;
  type: string;
  outcome: 'accepted' | 'declined' | 'withdrawn' | null;
  reason: string | null;
  made: string | null;
  withdrawableUntil: string;
}

/** An errand's status check: its outcome is in the representation, whatever the type. */
const errandStatus = {
  url: ({ id }: { id: string }) => `/api/errands/${id}`,
  outcome: (errand: Errand) => {
    switch (errand.outcome) {
      case 'accepted':
        return { outcome: 'accepted', made: errand.made ?? undefined } as const;
      case 'declined':
        return { outcome: 'declined', detail: errand.reason ?? undefined } as const;
      case 'withdrawn':
        return { outcome: 'withdrawn' } as const;
      default:
        return undefined; // pending
    }
  },
};

export interface PlaceHoldPayload {
  patronId: string;
  titleId: number;
  branchCode: string;
}

/**
 * Placing a hold: an errand (PROTOCOL §5). The intent's id is the errand's; the hold gets
 * its own, which the outcome says (`made`), and the guess is replaced when memory has it.
 */
export const placeHold = defineIntentType<PlaceHoldPayload, Hold, Errand>({
  name: 'place-hold',
  // patronId is in the payload for the read keys only. The server takes it from the session.
  request: ({ id, payload: { titleId, branchCode } }) => ({
    method: 'PUT',
    url: `/api/errands/${id}`,
    body: { type: 'place-hold', titleId, branchCode },
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
  status: errandStatus,
  invalidates: ({ payload }) => holdKeys(payload.patronId, payload.titleId),
});

export interface CancelHoldPayload {
  holdId: string;
  patronId: string;
  titleId: number;
}

/**
 * Cancelling a placed hold: a forward errand, not an undo. It makes nothing, so there's no
 * guess. Its promotion is the opposite of placing: done, and the hold is gone from memory
 * (see App).
 */
export const cancelHold = defineIntentType<CancelHoldPayload, never, Errand>({
  name: 'cancel-hold',
  request: ({ id, payload: { holdId } }) => ({
    method: 'PUT',
    url: `/api/errands/${id}`,
    body: { type: 'cancel-hold', holdId },
  }),
  receipt: ({ payload }) =>
    `We've received your request to cancel your hold on ${titleName(payload.titleId)}.`,
  apology: {
    say: ({ payload }, failure) =>
      `We couldn't cancel your hold on ${titleName(payload.titleId)}${failure.detail ? `: ${failure.detail}` : '.'}`,
    actions: [],
  },
  status: errandStatus,
  invalidates: ({ payload }) => holdKeys(payload.patronId, payload.titleId),
});
