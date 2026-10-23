import { HttpClient } from '@angular/common/http';
import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import { OutboxStore } from './outbox-store';
import { INTENT_STORAGE, indexedDbIntentStorage, IntentStorage } from './intent-storage';

/*
  Durability, the cheap part (PROTOCOL: Durability, proposed).

  An intent belongs to this browser until the server answers it. Storing it only stretches
  that across a reload: the tab closed before the answer came, and on the way back we can
  ask what became of it. Nothing stored is ever resent here; a restored intent is only
  status-checked. Resending, a time limit on old requests, one tab at a time, and clearing
  on sign-out are later steps.
*/

export interface IntentStorageOptions {
  /** The session resource (PROTOCOL §0), e.g. `/api/session`. */
  readonly sessionUrl: string;
  /**
   * Who the session says is signed in: a stable id, or undefined when signed out. The field
   * is the server's choice (the reference server's is `patronId`).
   */
  readonly userOf: (session: never) => string | undefined;
  /** Default: IndexedDB. */
  readonly storage?: IntentStorage;
}

/**
 * Stores intents across page loads. On startup, asks the session who's signed in, puts
 * that user's stored intents back in the outbox, and checks on the ones still waiting.
 * Until the session answers (or while signed out), intents are sent but not stored.
 */
export function provideIntentStorage(options: IntentStorageOptions): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: INTENT_STORAGE, useFactory: () => options.storage ?? indexedDbIntentStorage() },
    provideEnvironmentInitializer(() => {
      const outbox = inject(OutboxStore);
      inject(HttpClient)
        .get<never>(options.sessionUrl)
        .subscribe({
          next: (session) => {
            const owner = options.userOf(session);
            if (owner) void outbox.restore(owner);
          },
          error: () => undefined, // no session answer: nothing restored, nothing stored
        });
    }),
  ]);
}
