import { covers } from './invalidation';

describe('covers (PROTOCOL §3 key matching)', () => {
  it.each([
    ['/holds', '/holds', true],
    ['/holds', '/holds?patronId=p1', true],
    ['/holds', '/holds/0b6e', true],
    ['/holds', '/holdsArchive', false],
    ['/holds?patronId=p1', '/holds?patronId=p1', true],
    ['/holds?patronId=p1', '/holds?patronId=p1&titleId=2', false],
    ['/holds/0b6e', '/holds', false],
    ['/', '/holds', true],
  ])('%s covers %s: %s', (key, readKey, expected) => {
    expect(covers(key, readKey)).toBe(expected);
  });
});
