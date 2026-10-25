import { describe, expect, it } from 'vitest';
import { describeAsk, describeLegal, humanise } from '../src/lib/asks/describe';
import type { RecordedAsk } from '../src/lib/protocol/recording.generated';

const labels = new Map([['t1', 'Team 1'], ['t2', 'Team 2']]);

const ask = (over: Partial<RecordedAsk> = {}): RecordedAsk => ({
  id: 'a1', question: 'act_or_end_phase', prompt: 'Act or end the phase', teams: ['t1'],
  allowPass: false, sealed: false, legalKind: 'options', answered: [],
  options: [{ id: 'o1', label: 'Advance', pieces: ['u1'] }], pieces: [], ...over,
});

describe('describeAsk', () => {
  it('humanises the question and names every team the Ask addresses', () => {
    const view = describeAsk(ask({ teams: ['t1', 't2'], answered: ['t2'] }), labels);
    expect(view.heading).toBe('Act or end phase');
    expect(view.prompt).toBe('Act or end the phase');
    expect(view.addressees).toEqual([
      { id: 't1', label: 'Team 1', answered: false },
      { id: 't2', label: 'Team 2', answered: true },
    ]);
    expect(view.waitingOn).toEqual(['Team 1']);
  });

  it('falls back to a short id for a team the recording does not name', () => {
    expect(describeAsk(ask({ teams: ['0198b7ac-1111-7000-8000-000000000001'] }), labels).addressees[0]!.label)
      .toBe('team 0198b7ac');
  });

  it('describes every legal kind from the fields the schema carries', () => {
    expect(describeLegal(ask())).toBe('1 option');
    expect(describeLegal(ask({ legalKind: 'options', options: [] }))).toBe('0 options');
    expect(describeLegal(ask({ legalKind: 'pieces', options: [], pieces: ['u1', 'u2'] }))).toBe('2 eligible pieces');
    expect(describeLegal(ask({ legalKind: 'allocation', options: [], pieces: ['m1', 'm2', 'm3'] }))).toBe('3 targets to allocate over');
    expect(describeLegal(ask({ legalKind: 'assignment', pieces: ['u1', 'u2'] }))).toBe('1 item over 2 targets');
    expect(describeLegal(ask({ legalKind: 'move_region', options: [], pieces: ['m1', 'm2'], allowance: 6 }))).toBe('2 models within 6"');
    expect(describeLegal(ask({ legalKind: 'token_spend', options: [], pieces: ['m1', 'm2'], allowance: 3 }))).toBe('a token spend of up to 3 over 2 models');
    expect(describeLegal(ask({ legalKind: 'confirmation', options: [] }))).toBe('yes or no');
    expect(describeLegal(ask({ legalKind: 'SomethingNew', options: [] }))).toBe('SomethingNew');
  });

  it('notes a pass, a sealed Ask, a reaction window and the frame it belongs to', () => {
    const view = describeAsk(
      ask({ allowPass: true, sealed: true, windowKind: 'charged', frameId: '0198b7ac-2222-7000-8000-000000000002' }),
      labels,
    );
    expect(view.notes).toEqual([
      'a pass is allowed',
      'sealed: answers hidden until it closes',
      'reaction window: charged',
      'in frame 0198b7ac',
    ]);
  });

  it('carries the option, piece and allowance detail through untouched', () => {
    const view = describeAsk(ask({ legalKind: 'move_region', options: [], pieces: ['m1'], allowance: 12 }), labels);
    expect(view.pieces).toEqual(['m1']);
    expect(view.allowance).toBe(12);
    expect(describeAsk(ask(), labels).options).toEqual([{ id: 'o1', label: 'Advance', pieces: ['u1'] }]);
    expect(describeAsk(ask(), labels).allowance).toBeNull();
  });

  it('humanises a snake_case question and leaves anything else alone', () => {
    expect(humanise('deploy_scouts')).toBe('Deploy scouts');
    expect(humanise('confirm')).toBe('Confirm');
    expect(humanise('')).toBe('');
  });
});
