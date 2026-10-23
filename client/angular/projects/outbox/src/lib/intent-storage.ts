import { InjectionToken } from '@angular/core';
import { Intent } from './types';

/** Where intents are kept between page loads. Each is stored with whose it is. */
export interface IntentStorage {
  /** The intents stored for this user. Nobody else's. */
  load(owner: string): Promise<Intent[]>;
  /** Store or replace one. Resolves once it's written. */
  save(owner: string, intent: Intent): Promise<void>;
  remove(id: string): Promise<void>;
}

export const INTENT_STORAGE = new InjectionToken<IntentStorage>('outbox intent storage');

/** Kept in memory only: for tests, or to see the behavior without IndexedDB. */
export function memoryIntentStorage(): IntentStorage {
  const rows = new Map<string, { owner: string; intent: Intent }>();
  return {
    load: async (owner) =>
      [...rows.values()].filter((row) => row.owner === owner).map((row) => row.intent),
    save: async (owner, intent) => void rows.set(intent.id, { owner, intent }),
    remove: async (id) => void rows.delete(id),
  };
}

/** One database per origin, one record per intent: `{ owner, intent }`, indexed by owner. */
export function indexedDbIntentStorage(name = 'outbox'): IntentStorage {
  let opened: Promise<IDBDatabase> | undefined;
  const database = () =>
    (opened ??= new Promise((resolve, reject) => {
      const request = indexedDB.open(name, 1);
      request.onupgradeneeded = () =>
        request.result
          .createObjectStore('intents', { keyPath: 'intent.id' })
          .createIndex('owner', 'owner');
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    }));

  // Resolves when the transaction completes, not when the request succeeds: only then is it
  // written. (Chrome's default durability is "relaxed": it survives the tab closing, maybe
  // not the machine crashing.)
  const run = <T>(mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T>) =>
    database().then(
      (db) =>
        new Promise<T>((resolve, reject) => {
          const transaction = db.transaction('intents', mode);
          const request = work(transaction.objectStore('intents'));
          transaction.oncomplete = () => resolve(request.result);
          transaction.onerror = () => reject(transaction.error);
        }),
    );

  return {
    load: (owner) =>
      run('readonly', (store) => store.index('owner').getAll(owner)).then((rows) =>
        rows.map((row: { intent: Intent }) => row.intent),
      ),
    save: (owner, intent) => run('readwrite', (store) => store.put({ owner, intent })).then(),
    remove: (id) => run('readwrite', (store) => store.delete(id)).then(),
  };
}
