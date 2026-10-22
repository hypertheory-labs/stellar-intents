import { ReadCache, ReadEntry } from './read-cache';

/** A server the test answers by hand: `answer(key, value)` resolves the oldest request for it. */
function fakeServer() {
  const pending: { key: string; resolve: (v: unknown) => void; reject: (e: unknown) => void }[] =
    [];
  return {
    fetch: (key: string) =>
      new Promise<unknown>((resolve, reject) => pending.push({ key, resolve, reject })),
    asked: () => pending.map((p) => p.key),
    async answer(key: string, value: unknown) {
      const i = pending.findIndex((p) => p.key === key);
      if (i < 0) throw new Error(`nobody asked for ${key}`);
      pending.splice(i, 1)[0].resolve(value);
      await flush();
    },
    async fail(key: string) {
      const i = pending.findIndex((p) => p.key === key);
      pending.splice(i, 1)[0].reject(new Error('500'));
      await flush();
    },
  };
}

const flush = () => new Promise((r) => setTimeout(r));

function setup() {
  const server = fakeServer();
  let clock = 1_000;
  const cache = new ReadCache(server.fetch, () => clock);
  const tick = (ms: number) => (clock += ms);
  return { server, cache, tick };
}

/** Watch a key, recording every entry the listener sees. */
function look(cache: ReadCache, key: string) {
  const seen: ReadEntry[] = [];
  const stop = cache.watch(key, (e) => seen.push(e));
  return { seen, latest: () => seen[seen.length - 1], stop };
}

describe('ReadCache', () => {
  it('fetches what someone looks at, and remembers when', async () => {
    const { server, cache } = setup();
    const holds = look(cache, '/holds?patronId=P0042');

    expect(server.asked()).toEqual(['/holds?patronId=P0042']);
    expect(holds.latest()).toMatchObject({ hasValue: false, fetching: true });

    await server.answer('/holds?patronId=P0042', ['h1']);
    expect(holds.latest()).toMatchObject({
      hasValue: true,
      value: ['h1'],
      fetchedAt: 1_000,
      fetching: false,
    });
  });

  it('two readers of one key share one request and one entry', async () => {
    const { server, cache } = setup();
    const a = look(cache, '/holds');
    const b = look(cache, '/holds');

    expect(server.asked()).toEqual(['/holds']);
    await server.answer('/holds', ['h1']);
    expect(a.latest().value).toEqual(['h1']);
    expect(b.latest().value).toEqual(['h1']);
  });

  describe('freshness comes from the stream, or it is not assumed', () => {
    it('not live: a value is stale-first: shown at once, and refetched when looked at again', async () => {
      const { server, cache, tick } = setup();
      const first = look(cache, '/holds');
      await server.answer('/holds', ['h1']);
      first.stop();
      tick(60_000);

      const again = look(cache, '/holds');
      expect(again.seen[0]).toMatchObject({ value: ['h1'], fetchedAt: 1_000, stale: true });
      expect(server.asked()).toEqual(['/holds']);
      await server.answer('/holds', ['h1', 'h2']);
      expect(again.latest()).toMatchObject({ value: ['h1', 'h2'], fetchedAt: 61_000 });
    });

    it('live: a value fetched while live stays fresh until an invalidation covers it', async () => {
      const { server, cache } = setup();
      cache.setLive(true);
      const first = look(cache, '/holds?patronId=P0042');
      await server.answer('/holds?patronId=P0042', ['h1']);
      first.stop();

      const again = look(cache, '/holds?patronId=P0042');
      expect(again.latest()).toMatchObject({ value: ['h1'], stale: false });
      expect(server.asked()).toEqual([]);

      cache.invalidate(['/holds?patronId=P0042']);
      expect(server.asked()).toEqual(['/holds?patronId=P0042']);
      expect(again.latest()).toMatchObject({ value: ['h1'], stale: true, fetching: true });
    });

    it('going quiet (the stream dropped) makes everything stale', async () => {
      const { server, cache } = setup();
      cache.setLive(true);
      const holds = look(cache, '/holds');
      await server.answer('/holds', ['h1']);

      cache.setLive(false);
      expect(holds.latest().stale).toBe(true);
    });

    it('going live re-checks everything being looked at (the subscribed rule)', async () => {
      const { server, cache } = setup();
      const holds = look(cache, '/holds');
      await server.answer('/holds', ['h1']);

      cache.setLive(true);
      expect(server.asked()).toEqual(['/holds']);
      await server.answer('/holds', ['h1']);
      expect(holds.latest().stale).toBe(false);
    });
  });

  describe('invalidation (PROTOCOL §3)', () => {
    it('only covered reads go stale, and only watched ones refetch', async () => {
      const { server, cache } = setup();
      cache.setLive(true);
      look(cache, '/holds?patronId=P0042');
      const waitList = look(cache, '/holds?titleId=1001');
      const archive = look(cache, '/holdsArchive');
      await server.answer('/holds?patronId=P0042', []);
      await server.answer('/holds?titleId=1001', []);
      await server.answer('/holdsArchive', []);
      waitList.stop(); // not on screen any more

      cache.invalidate(['/holds']);

      expect(server.asked()).toEqual(['/holds?patronId=P0042']);
      expect(cache.peek('/holds?titleId=1001').stale).toBe(true);
      expect(archive.latest().stale).toBe(false);
    });

    it('an invalidation during a fetch distrusts that answer and asks again', async () => {
      const { server, cache } = setup();
      cache.setLive(true);
      const holds = look(cache, '/holds');

      cache.invalidate(['/holds']); // the change happened after we asked, maybe after it answered
      await server.answer('/holds', ['old']);

      expect(holds.latest()).toMatchObject({ value: ['old'], stale: true });
      expect(server.asked()).toEqual(['/holds']);
      await server.answer('/holds', ['new']);
      expect(holds.latest()).toMatchObject({ value: ['new'], stale: false });
    });

    it('never holds candidates: it only ever has what the server answered', async () => {
      const { server, cache } = setup();
      const holds = look(cache, '/holds');
      await server.answer('/holds', ['h1']);
      expect(holds.seen.map((e) => e.value).filter(Boolean)).toEqual([['h1']]);
    });
  });

  describe('errors', () => {
    it('a failed first load is an error with no value', async () => {
      const { server, cache } = setup();
      const holds = look(cache, '/holds');
      await server.fail('/holds');
      expect(holds.latest()).toMatchObject({ hasValue: false, fetching: false });
      expect(holds.latest().error).toBeInstanceOf(Error);
    });

    it('a failed refetch keeps the last value: still the last thing the server said', async () => {
      const { server, cache } = setup();
      cache.setLive(true);
      const holds = look(cache, '/holds');
      await server.answer('/holds', ['h1']);

      cache.invalidate(['/holds']);
      await server.fail('/holds');

      expect(holds.latest()).toMatchObject({
        hasValue: true,
        value: ['h1'],
        fetchedAt: 1_000,
        stale: true,
      });
      expect(holds.latest().error).toBeInstanceOf(Error);
    });
  });
});
