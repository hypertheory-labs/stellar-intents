import { provideHttpClient, withFetch } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import {
  provideIntentTypes,
  provideOutboxStatusChecks,
  provideOutboxStream,
  provideReads,
} from 'outbox';
import { cancelHold, placeHold } from './holds';

// Everything is same-origin under /api: the dev server proxies it to the reference server
// (proxy.conf.json), the way an edge would. So the session cookie just works.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch()), // withFetch: the read cache's refetches use cache: 'no-cache'
    provideIntentTypes(placeHold, cancelHold),
    provideOutboxStatusChecks(),
    provideOutboxStream({ eventsUrl: '/api/events', sessionUrl: '/api/session' }),
    provideReads({ apiRoot: '/api' }),
  ],
};
