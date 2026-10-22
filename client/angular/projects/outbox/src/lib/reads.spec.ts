import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { injectDispatch } from '@ngrx/signals/events';
import { invalidationEvents } from './invalidation';
import { cachedHttpResource, provideReads } from './reads';
import { serverEvents } from './stream';

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideReads({ apiRoot: '/api' })],
  });
  const http = TestBed.inject(HttpTestingController);
  const server = TestBed.runInInjectionContext(() => injectDispatch(serverEvents));
  const reads = TestBed.runInInjectionContext(() => injectDispatch(invalidationEvents));
  return { http, server, reads };
}

/** Lets effects run, and an answered request's promise settle. */
async function settle() {
  TestBed.tick();
  await new Promise((r) => setTimeout(r));
  TestBed.tick();
}

describe('cachedHttpResource', () => {
  it('reads through the cache: loading, then resolved, with when it was fetched', async () => {
    const { http } = setup();
    const holds = TestBed.runInInjectionContext(() => cachedHttpResource<string[]>(() => '/holds'));
    TestBed.tick();

    expect(holds.status()).toBe('loading');
    const req = http.expectOne('/api/holds');
    expect(req.request.cache).toBe('no-cache'); // past the browser's HTTP cache, to the server
    req.flush(['h1']);
    await settle();

    expect(holds.status()).toBe('resolved');
    expect(holds.value()).toEqual(['h1']);
    expect(holds.fetchedAt()).toEqual(expect.any(Number));
  });

  it('nothing to read (undefined key) is idle, and asks nothing', () => {
    const { http } = setup();
    const holds = TestBed.runInInjectionContext(() =>
      cachedHttpResource<string[]>(() => undefined),
    );
    TestBed.tick();

    expect(holds.status()).toBe('idle');
    http.expectNone(() => true);
  });

  it('two readers share one request', async () => {
    const { http } = setup();
    const a = TestBed.runInInjectionContext(() => cachedHttpResource<string[]>(() => '/holds'));
    const b = TestBed.runInInjectionContext(() => cachedHttpResource<string[]>(() => '/holds'));
    TestBed.tick();

    http.expectOne('/api/holds').flush(['h1']);
    await settle();
    expect(a.value()).toEqual(['h1']);
    expect(b.value()).toEqual(['h1']);
  });

  it('a new key reads the new key', async () => {
    const { http } = setup();
    const patron = signal('P0042');
    const holds = TestBed.runInInjectionContext(() =>
      cachedHttpResource<string[]>(() => `/holds?patronId=${patron()}`),
    );
    TestBed.tick();
    http.expectOne('/api/holds?patronId=P0042').flush(['h1']);
    await settle();

    patron.set('P0043');
    TestBed.tick();
    http.expectOne('/api/holds?patronId=P0043').flush(['h9']);
    await settle();
    expect(holds.value()).toEqual(['h9']);
  });

  describe('invalidation, from either side', () => {
    async function liveWithAValue() {
      const ctx = setup();
      ctx.server.subscribed(); // the stream is live
      const holds = TestBed.runInInjectionContext(() =>
        cachedHttpResource<string[]>(() => '/holds?patronId=P0042'),
      );
      TestBed.tick();
      ctx.http.expectOne('/api/holds?patronId=P0042').flush(['h1']);
      await settle();
      expect(holds.stale()).toBe(false);
      return { ...ctx, holds };
    }

    it('from the server: stale-first, then the new value', async () => {
      const { http, reads, holds } = await liveWithAValue();

      reads.invalidated({ keys: ['/holds?patronId=P0042'], from: 'server' });
      expect(holds.status()).toBe('reloading');
      expect(holds.value()).toEqual(['h1']); // still showing what we had

      http.expectOne('/api/holds?patronId=P0042').flush(['h1', 'h2']);
      await settle();
      expect(holds.value()).toEqual(['h1', 'h2']);
      expect(holds.stale()).toBe(false);
    });

    it('from an intent type: the same keys, the same effect', async () => {
      const { http, reads } = await liveWithAValue();
      reads.invalidated({ keys: ['/holds?patronId=P0042'], from: 'intent' });
      http.expectOne('/api/holds?patronId=P0042');
    });

    it('a key that does not cover the read leaves it alone', async () => {
      const { http, reads, holds } = await liveWithAValue();
      reads.invalidated({ keys: ['/holds?titleId=1001'], from: 'server' });
      http.expectNone('/api/holds?patronId=P0042');
      expect(holds.stale()).toBe(false);
    });

    it('the stream dropping makes it stale (nobody is hearing about changes)', async () => {
      const { server, holds } = await liveWithAValue();
      server.disconnected();
      expect(holds.stale()).toBe(true);
    });
  });

  it('a failed refetch keeps the value, and says the refresh failed', async () => {
    const { http, reads } = setup();
    const holds = TestBed.runInInjectionContext(() => cachedHttpResource<string[]>(() => '/holds'));
    TestBed.tick();
    http.expectOne('/api/holds').flush(['h1']);
    await settle();

    reads.invalidated({ keys: ['/holds'], from: 'server' });
    http.expectOne('/api/holds').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(holds.status()).toBe('resolved');
    expect(holds.value()).toEqual(['h1']);
    expect(holds.refreshError()).toBeDefined();
  });
});
