import { isCandidate, mergeWithCandidates, readyToRetire, realIdOf } from './promotion';
import { Candidate, Intent, IntentState } from './types';

interface Hold {
  id: string;
  status: string;
}

const memory: Hold[] = [
  { id: 'a', status: 'placed' },
  { id: 'b', status: 'requested' },
];
const guess = (id: string, realId = id): Candidate<Hold> => ({
  kind: 'candidate',
  intentId: id,
  id: realId,
  value: { id: realId, status: 'requested' },
});
const intent = (id: string, state: IntentState): Intent => ({
  id,
  type: 'place-hold',
  payload: {},
  requestedAt: 0,
  state,
  schema: 2,
});
const accepted = (id: string, made?: string) =>
  intent(id, { status: 'decided', decidedAt: 1, outcome: 'accepted', made });

describe('promotion', () => {
  it('shows a guess until its real version arrives, newest first', () => {
    const merged = mergeWithCandidates(memory, [guess('c')], (h) => h.id);
    expect(merged.map((m) => (isCandidate(m) ? `guess:${m.intentId}` : m.id))).toEqual([
      'guess:c',
      'a',
      'b',
    ]);
  });

  it('drops a guess the moment memory has its id (never both, never neither)', () => {
    const merged = mergeWithCandidates(memory, [guess('b')], (h) => h.id);
    expect(merged.filter(isCandidate)).toEqual([]);
    expect(merged).toEqual(memory);
  });

  it('never edits memory', () => {
    const before = structuredClone(memory);
    mergeWithCandidates(memory, [guess('c')], (h) => h.id);
    expect(memory).toEqual(before);
  });

  it('a decided intent is ready to retire only once memory contains it', () => {
    expect(readyToRetire([accepted('b'), accepted('c')], memory, (h) => h.id)).toEqual(['b']);
  });

  // PROTOCOL §5: an errand's id isn't its hold's
  it("when the outcome says what it made, that's the id memory must contain", () => {
    expect(readyToRetire([accepted('errand-1', 'a')], memory, (h) => h.id)).toEqual(['errand-1']);
    expect(readyToRetire([accepted('errand-2', 'z')], memory, (h) => h.id)).toEqual([]);
    expect(realIdOf(accepted('errand-1', 'a'))).toBe('a');
    expect(realIdOf(intent('errand-3', { status: 'received', receivedAt: 1 }))).toBe('errand-3');
  });

  it("drops the guess of an intent whose outcome made memory's item", () => {
    const merged = mergeWithCandidates(memory, [guess('errand-1', 'a')], (h) => h.id);
    expect(merged).toEqual(memory);
  });
});
