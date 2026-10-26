import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import {
  addEntities,
  addEntity,
  removeEntity,
  updateEntity,
  withEntities,
} from '@ngrx/signals/entities';
import { injectDispatch } from '@ngrx/signals/events';
import { outboxEvents } from './events';
import { INTENT_STORAGE } from './intent-storage';
import { invalidationEvents } from './invalidation';
import { realIdOf } from './promotion';
import { AnyIntentType, INTENT_TYPES } from './registry';
import {
  ApologyAction,
  Candidate,
  Failure,
  Intent,
  IntentState,
  IntentStatus,
  IntentType,
  Outcome,
  Remedy,
} from './types';

/** How long to wait for an answer before an intent is `unknown`, unless its type says. */
const DEFAULT_TIMEOUT_MS = 30_000;

/** What the UI can show about a pending intent. Always true. */
export interface Receipt {
  readonly intentId: string;
  readonly status: IntentStatus;
  readonly text: string;
}

/** What the UI owes the user about an intent whose guess didn't hold, or might not have. */
export interface ApologyNotice {
  readonly intentId: string;
  readonly status: IntentStatus;
  /** What the person can do next, so a UI can offer it without knowing the intent type. */
  readonly remedy: Remedy;
  readonly message: string;
  readonly actions: readonly ApologyAction[];
}

/**
 * Holds intents (in memory, and across page loads if there's storage: see
 * provideIntentStorage), sends them, and walks each through its lifecycle,
 * publishing an event at every step. It owns the guesses, never the app's data:
 * features read receipts, candidates, and apologies from here, and their own data
 * from their own resources.
 */
export const OutboxStore = signalStore(
  { providedIn: 'root' },

  withEntities<Intent>(),
  withProps(() => ({
    _types: new Map(
      (inject(INTENT_TYPES, { optional: true }) ?? []).map((t) => [t.name, t] as const),
    ),
    _http: inject(HttpClient),
    _dispatch: injectDispatch(outboxEvents),
    _invalidate: injectDispatch(invalidationEvents),
    _storage: inject(INTENT_STORAGE, { optional: true }),
    /** Whose intents these are, once the session has said (see provideIntentStorage). */
    _owner: { id: undefined as string | undefined },
    /** Intents requested since this page loaded are ones it may resend on its own. */
    _pageStartedAt: Date.now(),
  })),
  withComputed((store) => {
    const typeOf = (intent: Intent) => store._types.get(intent.type)!;
    return {
      /** Every intent not yet retired or dismissed, with something true to say about it. */
      receipts: computed<Receipt[]>(() =>
        store.entities().map((intent) => ({
          intentId: intent.id,
          status: intent.state.status,
          text: typeOf(intent).receipt(intent),
        })),
      ),
      /** Intents that failed, or whose outcome we don't know, with their declared apology. */
      apologies: computed<ApologyNotice[]>(() =>
        store.entities().flatMap((intent) => {
          const failure = failureOf(intent.state);
          if (!failure) return [];
          const { apology } = typeOf(intent);
          return [
            {
              intentId: intent.id,
              status: intent.state.status,
              remedy: failure.remedy,
              message: apology.say(intent, failure),
              actions: apology.actions,
            },
          ];
        }),
      ),
    };
  }),
  withMethods((store) => {
    const transitionEvents = {
      requested: store._dispatch.intentRequested,
      sent: store._dispatch.intentSent,
      received: store._dispatch.intentReceived,
      decided: store._dispatch.intentDecided,
      unknown: store._dispatch.intentUnknown,
      interrupted: store._dispatch.intentInterrupted,
    } satisfies Record<IntentStatus, unknown>;

    function current(id: string) {
      return store.entityMap()[id];
    }

    /** Write it down, if there's storage and we know whose it is. Resolves once written. */
    function persist(intent: Intent | undefined): Promise<void> {
      const owner = store._owner.id;
      if (!intent || !owner || !store._storage) return Promise.resolve();
      return store._storage.save(owner, intent).catch(() => undefined);
    }

    function forget(id: string) {
      store._storage?.remove(id).catch(() => undefined);
    }

    function moveTo(id: string, state: IntentState) {
      const now = current(id);
      if (!now) return; // retired or dismissed while in flight: nothing to update
      // A decision is final. Answers can arrive in any order (a slow 202 after a status check
      // or a server event already said "done"), and a late one must not undo what's known.
      if (now.state.status === 'decided') return;
      patchState(store, updateEntity({ id, changes: { state } }));
      void persist(current(id));
      transitionEvents[state.status]({ intent: current(id) });
      // Withdrawn: the person took it back themselves. No receipt, guess, or apology is
      // owed, so once it's been announced there's nothing left to hold.
      if (state.status === 'decided' && state.outcome === 'withdrawn') {
        patchState(store, removeEntity(id));
        forget(id);
      }
    }

    function settle(id: string, outcome: Outcome) {
      const decidedAt = Date.now();
      moveTo(
        id,
        outcome.outcome === 'declined'
          ? {
              status: 'decided',
              decidedAt,
              outcome: 'declined',
              failure: { remedy: 'no', detail: outcome.detail },
            }
          : outcome.outcome === 'accepted'
            ? { status: 'decided', decidedAt, outcome: 'accepted', made: outcome.made }
            : { status: 'decided', decidedAt, outcome: 'withdrawn' },
      );
    }

    /** PROTOCOL §2: ask what became of it, if its type says how. */
    function checkStatus(id: string) {
      const intent = current(id);
      const check = intent && store._types.get(intent.type)?.status;
      if (!check) return;
      store._http.get(check.url(intent)).subscribe({
        next: (body) => {
          const outcome = check.outcome(body, intent);
          if (outcome) settle(id, outcome);
          // undefined: not decided yet; the receipt is still true
        },
        // A 404 means "not recorded yet", not an outcome: it may still be in flight. Any
        // other failure is just a check that didn't work. Either way, ask again later.
        error: () => undefined,
      });
    }

    // An interrupted intent may have landed after all (a 5xx after the commit), so it's
    // asked about too.
    function checkWaiting() {
      store
        .entities()
        .filter((i) => ['received', 'unknown', 'interrupted'].includes(i.state.status))
        .forEach((i) => checkStatus(i.id));
    }

    /** Send an interrupted intent again, with the same id. */
    function resend(id: string) {
      const intent = current(id);
      const type = intent && store._types.get(intent.type);
      if (intent?.state.status === 'interrupted' && type) send(intent, type);
    }

    function send(intent: Intent, type: AnyIntentType) {
      const { method, url, body } = type.request(intent);
      moveTo(intent.id, { status: 'sent', sentAt: Date.now() });

      // A timeout doesn't cancel the request: it may still land, and a late answer
      // moves the intent on from `unknown`. Unknown is "we don't know yet", not failure.
      const timer = setTimeout(() => {
        if (current(intent.id)?.state.status === 'sent') {
          moveTo(intent.id, { status: 'unknown', sentAt: Date.now() });
        }
      }, type.timeoutMs ?? DEFAULT_TIMEOUT_MS);

      store._http
        .request(method, url, {
          body,
          observe: 'response',
          headers: { 'Idempotency-Key': intent.id },
        })
        .subscribe({
          next: (response: HttpResponse<unknown>) => {
            clearTimeout(timer);
            if (response.status === 202) {
              moveTo(intent.id, { status: 'received', receivedAt: Date.now() });
              return;
            }
            // Decided, but not necessarily accepted: a repeat of a declined errand is a 200
            // too. The body says how it ended, read the way a status check reads it
            // (PROTOCOL §1). No body, or no way to read one: it's done, and good.
            const outcome =
              type.status && response.body != null
                ? type.status.outcome(response.body, intent)
                : ({ outcome: 'accepted' } as const);
            if (outcome) settle(intent.id, outcome);
            else moveTo(intent.id, { status: 'received', receivedAt: Date.now() });
          },
          error: (error: HttpErrorResponse) => {
            clearTimeout(timer);
            moveTo(intent.id, stateForError(error));
          },
        });
    }

    return {
      /**
       * The moment a draft becomes an intent. Returns the intent, whose id is also
       * the id the real item will have.
       */
      enqueue<TPayload>(type: IntentType<TPayload, unknown>, payload: TPayload): Intent<TPayload> {
        const registered = store._types.get(type.name);
        if (registered !== type) {
          throw new Error(
            `Intent type '${type.name}' isn't registered (provideIntentTypes in app.config). ` +
              `An unregistered intent couldn't be resumed or explained later.`,
          );
        }
        const intent: Intent<TPayload> = {
          id: crypto.randomUUID(),
          type: type.name,
          payload,
          requestedAt: Date.now(),
          state: { status: 'requested' },
          schema: 2,
        };
        // the store holds every type's intents; it never reads the payload
        patchState(store, addEntity(intent as Intent));
        store._dispatch.intentRequested({ intent });
        // PROTOCOL (Durability): stored before it's sent, or a tab closed mid-send leaves
        // nothing to ask about. Without storage, it goes at once.
        if (store._storage && store._owner.id) {
          void persist(intent as Intent).then(() =>
            send(intent as Intent, registered as AnyIntentType),
          );
        } else {
          send(intent as Intent, registered as AnyIntentType);
        }
        return intent;
      },

      /** Candidates of one intent type: guesses, wrapped so they can't pass for the real thing. */
      candidates<TCandidate>(type: IntentType<never, TCandidate>): Candidate<TCandidate>[] {
        if (!type.candidate) return [];
        return store
          .entities()
          .filter((intent) => intent.type === type.name && hasCandidate(intent.state))
          .map((intent) => ({
            kind: 'candidate',
            intentId: intent.id,
            id: realIdOf(intent),
            value: type.candidate!(intent as Intent<never>),
          }));
      },

      /**
       * For an intent whose outcome the app learned some other way. (The outbox's own ways
       * are `checkStatus` and the event stream; see provideOutboxStream.)
       */
      resolve(id: string, outcome: Outcome) {
        settle(id, outcome);
      },

      /** The server decided (PROTOCOL §3, intent-decided). Ids it doesn't hold are ignored. */
      settle,

      /** Ask the server about one intent (PROTOCOL §2). Needs the type's `status`. */
      checkStatus,

      /**
       * Ask about everything still waiting: `received` (accepted, outcome later) and
       * `unknown` (no answer in time; it may have landed). The floor: on page-visible, and
       * on every (re)connect of the event stream.
       */
      checkWaiting,

      /**
       * Whose intents these are, from now on (the session said). Puts that user's stored
       * intents back, and asks about the ones still waiting. Nothing is resent: a request
       * from a page that's gone is `unknown` until a status check or the server says.
       */
      async restore(owner: string) {
        store._owner.id = owner;
        const stored = (await store._storage?.load(owner)) ?? [];
        const restored = stored
          // already here (this page enqueued it), or of a type this build doesn't know
          .map(upcast)
          .filter((intent) => !current(intent.id) && store._types.has(intent.type))
          .map((intent) => ({ ...intent, state: resumed(intent.state) }));
        patchState(store, addEntities(restored));
        restored.forEach((intent) => void persist(intent));
        checkWaiting();
      },

      /**
       * Send an interrupted intent again, with the same id: the person said "try again",
       * or they've signed back in.
       */
      resend,

      /**
       * The client may deliver what the person asked for on its own, but only what was
       * asked on this page. One restored from storage may be from yesterday, so it waits
       * for the person. Called when the stream (re)connects: the server is reachable, and
       * the session is valid.
       */
      resendInterrupted() {
        store
          .entities()
          .filter((i) => i.state.status === 'interrupted' && i.requestedAt >= store._pageStartedAt)
          .forEach((i) => resend(i.id));
      },

      /** Promotion: the feature's memory now contains the real item. */
      retire(id: string) {
        const intent = current(id);
        if (!intent) return;
        patchState(store, removeEntity(id));
        forget(id);
        store._dispatch.intentRetired({ intent });
      },

      /** The user dismissed the apology. */
      dismiss(id: string) {
        const intent = current(id);
        if (!intent) return;
        patchState(store, removeEntity(id));
        forget(id);
        store._dispatch.intentDismissed({ intent });
      },
    };
  }),
);

/** The failure an apology should speak to, if the state has one. */
function failureOf(state: IntentState): Failure | undefined {
  switch (state.status) {
    case 'decided':
      return state.outcome === 'declined' ? state.failure : undefined;
    case 'interrupted':
      return state.failure;
    case 'unknown':
      return { remedy: 'wait' };
    default:
      return undefined;
  }
}

/**
 * An intent read back from storage. Nothing from the page that stored it is in flight any
 * more: if it was sent (or about to be) without an answer, we don't know whether it landed.
 */
function resumed(state: IntentState): IntentState {
  switch (state.status) {
    case 'requested':
      return { status: 'unknown', sentAt: Date.now() };
    case 'sent':
      return { status: 'unknown', sentAt: state.sentAt };
    default:
      return state;
  }
}

/** A guess is still worth showing until the intent is decided otherwise, or retired. */
function hasCandidate(state: IntentState) {
  return state.status !== 'decided' || state.outcome === 'accepted';
}

/**
 * A stored intent in today's shape. Schema 1 had two final states, `done` and
 * `needs-attention`; schema 2 has one, `decided`, with how it ended.
 */
function upcast(stored: Intent): Intent {
  if (stored.schema === 2) return stored;
  const state = stored.state as unknown as
    | { status: 'done'; doneAt: number }
    | { status: 'needs-attention'; failure: Failure }
    | IntentState;
  const upcasted: IntentState =
    state.status === 'done'
      ? { status: 'decided', decidedAt: state.doneAt, outcome: 'accepted' }
      : state.status === 'needs-attention'
        ? { status: 'decided', decidedAt: Date.now(), outcome: 'declined', failure: state.failure }
        : state;
  return { ...stored, state: upcasted, schema: 2 };
}

// WHY only a 4xx (other than 401) is final: it's the server deciding. No response, a 5xx,
// and a 401 aren't decisions, so the intent stays alive and goes again with the same id.
// A new id would be a new request, and a duplicate if the first one landed after all.
function stateForError(error: HttpErrorResponse): IntentState {
  if (error.status === 0) {
    return { status: 'interrupted', failure: { remedy: 'try-again' } };
  }
  const body = error.error as { detail?: unknown } | null;
  const detail = typeof body?.detail === 'string' ? body.detail : undefined;
  if (error.status === 401) {
    return { status: 'interrupted', failure: { remedy: 'sign-in', status: 401, detail } };
  }
  if (error.status >= 500) {
    return {
      status: 'interrupted',
      failure: { remedy: 'try-again', status: error.status, detail },
    };
  }
  return {
    status: 'decided',
    decidedAt: Date.now(),
    outcome: 'declined',
    failure: { remedy: 'no', status: error.status, detail },
  };
}
