/*
 * Public API Surface of outbox
 */

export * from './lib/types';
export { outboxEvents } from './lib/events';
export { provideIntentTypes } from './lib/registry';
export type { AnyIntentType } from './lib/registry';
export { OutboxStore } from './lib/outbox-store';
export type { ApologyNotice, Receipt } from './lib/outbox-store';
export { isCandidate, mergeWithCandidates, readyToRetire, realIdOf } from './lib/promotion';
export { covers, invalidationEvents } from './lib/invalidation';
export { ReadCache } from './lib/read-cache';
export type { ReadEntry } from './lib/read-cache';
export { cachedHttpResource, provideReads, Reads } from './lib/reads';
export type { CachedHttpResource, ReadsOptions } from './lib/reads';
export {
  EVENT_SOURCE,
  provideOutboxStatusChecks,
  provideOutboxStream,
  serverEvents,
} from './lib/stream';
export type { EventSourceLike, OutboxStreamOptions } from './lib/stream';
export { provideIntentStorage } from './lib/durability';
export type { IntentStorageOptions } from './lib/durability';
export { INTENT_STORAGE, indexedDbIntentStorage, memoryIntentStorage } from './lib/intent-storage';
export type { IntentStorage } from './lib/intent-storage';
