/*
  The outbox's vocabulary, as types. See this library's README.

  Two kinds of thing live here, and the split is deliberate:

  - DATA (Intent, IntentState, Failure): plain and serializable, because later
    phases store intents in IndexedDB and read them back in a new session. No
    functions, no class instances, no Dates (timestamps are epoch ms).

  - BEHAVIOR (IntentType): how to send an intent, what to say about it, what it
    might become, and how to apologize. Registered once per type, in code. A
    stored intent refers to its type by name, so behavior is never persisted.
*/

// ---------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------

/** What the user asked for, as a message. */
export interface Intent<TPayload = unknown> {
  /** Client-generated (crypto.randomUUID()). Also the idempotency key. */
  readonly id: string;
  /** The registered IntentType's name, e.g. 'place-hold'. */
  readonly type: string;
  readonly payload: TPayload;
  /** When the user asked. Epoch ms. */
  readonly requestedAt: number;
  readonly state: IntentState;
  /** Shape version of this record, so stored intents can be upcast (see `upcast`). */
  readonly schema: 2;
}

/**
 * Where an intent is in its life: the worksheet's Part 2 diagram.
 *
 *   requested ─► sent ─► received ─► decided   (final: accepted, declined, or withdrawn)
 *                    ├─► unknown                     (no answer in time)
 *                    └─► interrupted                 (nothing decided: send again, same id)
 *
 * (Drafting happens before there is an intent at all: that's the form's state.)
 */
export type IntentState =
  | { readonly status: 'requested' }
  | { readonly status: 'sent'; readonly sentAt: number }
  | { readonly status: 'unknown'; readonly sentAt: number }
  | { readonly status: 'received'; readonly receivedAt: number }
  | {
      readonly status: 'decided';
      readonly decidedAt: number;
      readonly outcome: 'accepted';
      /** The id of what it made, when that isn't the intent's own (an errand's hold). */
      readonly made?: string;
    }
  /** Decided against. The apology's remedy is `no`. */
  | {
      readonly status: 'decided';
      readonly decidedAt: number;
      readonly outcome: 'declined';
      readonly failure: Failure;
      /** What the server says can be chosen instead (PROTOCOL §1, Offers). */
      readonly offers?: readonly OfferGroup[];
    }
  /**
   * The person took it back before it was decided (a cancel that got there first). Nothing
   * is owed, so the outbox lets it go as soon as it's announced.
   */
  | { readonly status: 'decided'; readonly decidedAt: number; readonly outcome: 'withdrawn' }
  /**
   * Nothing was decided and nothing is in flight: no response, a 5xx, or a 401. Not
   * final. It goes again with the same id (PROTOCOL: Remedies).
   */
  | { readonly status: 'interrupted'; readonly failure: Failure };

export type IntentStatus = IntentState['status'];

/**
 * What the person can do about it. Apologies are sorted by this, not by what went wrong
 * underneath: the reasons belong to the business and can't be counted, but what to do
 * next can (PROTOCOL: Remedies).
 *
 * - `try-again`: it didn't get through (no response), or the server failed (5xx).
 * - `wait`: no answer in time. It may have landed; we're still finding out.
 * - `no`: the server said no. Why is in `detail`.
 * - `sign-in`: the session has ended (401).
 */
export type Remedy = 'try-again' | 'wait' | 'no' | 'sign-in';

/** What went wrong, as data an apology can be specific about. */
export interface Failure {
  readonly remedy: Remedy;
  /** HTTP status, when there was one. */
  readonly status?: number;
  /** Server-provided detail (e.g. a problem+json `detail`), when there was one. */
  readonly detail?: string;
}

/**
 * A decline's offers (PROTOCOL §1, Offers): whole errands the person can choose one of
 * instead, under one id. The client never builds one: it sends a choice back as it came,
 * with `takeOffer`.
 */
export interface OfferGroup {
  readonly id: string;
  readonly choose: readonly unknown[];
}

// ---------------------------------------------------------------------------
// Guesses
// ---------------------------------------------------------------------------

/**
 * Something shaped like what an intent will become, staged before the server
 * answers. A wrapper, not a T, so a template can't render it as the real thing
 * by accident: you have to reach through `.value`, on purpose.
 */
export interface Candidate<T> {
  readonly kind: 'candidate';
  /** The intent this guess belongs to. */
  readonly intentId: string;
  /**
   * The id the real item has, or will have: the intent's own, unless its outcome said it
   * made something else (PROTOCOL §3, `made`).
   */
  readonly id: string;
  readonly value: T;
}

// ---------------------------------------------------------------------------
// Behavior
// ---------------------------------------------------------------------------

/** What the outbox needs to send an intent. Deliberately not an HttpRequest. */
export interface IntentRequest {
  readonly method: 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  readonly url: string;
  readonly body?: unknown;
}

/** How it ended, as a status check or the server's event stream reports it (PROTOCOL §3). */
export type Outcome =
  | { readonly outcome: 'accepted'; readonly made?: string }
  | {
      readonly outcome: 'declined';
      readonly detail?: string;
      readonly offers?: readonly OfferGroup[];
    }
  | { readonly outcome: 'withdrawn' };

/**
 * How to ask the server what became of an intent (PROTOCOL §2). Declared per type,
 * because whether a representation means "done" is the resource's business: a hold's
 * `status` is `placed` or `rejected`.
 */
export interface StatusCheck<TPayload, TStatus> {
  /** Where to ask. For a create, the resource's own URL (its id is the intent's id). */
  url(intent: Intent<TPayload>): string;
  /** What the answer means. `undefined`: not decided yet, and the receipt is still true. */
  outcome(body: TStatus, intent: Intent<TPayload>): Outcome | undefined;
}

/** What the user can do about an apology. Dismissing is always available. */
export type ApologyAction = 'retry' | 'edit' | 'discard' | 'contact';

/** Declared with the intent type. If you can't describe it, you haven't earned the guess. */
export interface Apology<TPayload> {
  /** What the user is told. Gets the intent and the failure, so it can be specific. */
  say(intent: Intent<TPayload>, failure: Failure): string;
  readonly actions: readonly ApologyAction[];
}

/** Everything the outbox knows about one kind of intent. Registered, never stored. */
export interface IntentType<TPayload, TCandidate = never, TStatus = unknown> {
  /** Stored on each intent; must stay stable across deploys. */
  readonly name: string;
  /** The request to send. Called each time the intent is sent. */
  request(intent: Intent<TPayload>): IntentRequest;
  /** Always true: "We've received your request to place a hold on Dune." */
  receipt(intent: Intent<TPayload>): string;
  /** Optional guess: what the request will look like if it succeeds. */
  candidate?(intent: Intent<TPayload>): TCandidate;
  readonly apology: Apology<TPayload>;
  /** How long to wait for an answer before the intent is `unknown`. */
  readonly timeoutMs?: number;
  /** Optional: how to ask what became of it. Without one, only the server's events can tell. */
  readonly status?: StatusCheck<TPayload, TStatus>;
}

/** Identity function that exists so TypeScript can infer TPayload and TCandidate. */
export function defineIntentType<TPayload, TCandidate = never, TStatus = unknown>(
  type: IntentType<TPayload, TCandidate, TStatus>,
): IntentType<TPayload, TCandidate, TStatus> {
  return type;
}
