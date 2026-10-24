import { type } from '@ngrx/signals';
import { eventGroup } from '@ngrx/signals/events';
import { Intent } from './types';

/**
 * Events out: every lifecycle transition, published globally, carrying the
 * intent as plain data. Zero or more handlers: a feature reloads its resource on
 * intentDecided, a toast shows an apology, analytics logs it. The outbox never knows.
 */
export const outboxEvents = eventGroup({
  source: 'Outbox',
  events: {
    intentRequested: type<{ intent: Intent }>(),
    intentSent: type<{ intent: Intent }>(),
    intentReceived: type<{ intent: Intent }>(),
    intentUnknown: type<{ intent: Intent }>(),
    /** Final: `intent.state.outcome` says how it ended. */
    intentDecided: type<{ intent: Intent }>(),
    intentInterrupted: type<{ intent: Intent }>(),
    /** Promotion: the feature's memory has the real item, so the guess is gone. */
    intentRetired: type<{ intent: Intent }>(),
    /** The user dismissed the intent (usually its apology). */
    intentDismissed: type<{ intent: Intent }>(),
  },
});
