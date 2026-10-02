import { describe, expect, it } from 'vitest';
import { RecordingError, parseRecording, readRecording, teamOrdinals } from '../src/lib/recording/read';
import { SCHEMA_VERSION } from '../src/lib/protocol/recording.generated';
import { readFileSync } from 'node:fs';

const minimal = () => ({
  schemaVersion: SCHEMA_VERSION,
  meta: {
    scenario: 'terrain', seed: 1, driver: 'in-process', gameRules: 'grimdark-future',
    propertiesHash: 'sha256:x', schedulerShape: 'alternating-activation', enforcement: 'strict',
    teams: [{ id: 't1', ordinal: 1, name: 'Team 1', participants: ['p1'] }],
    outcome: 'round_limit', rounds: 1, lastSeq: 1, startedAt: '1970-01-01T00:00:00+00:00', durationMs: 1,
  },
  board: {
    width: 72, length: 48, texture: 'grass', terrain: [], markers: [], zones: [],
    models: [{ id: 'm1', unitId: 'u1', unitName: 'Rifles', name: 'Rifleman', ordinal: 1, teamId: 't1', teamOrdinal: 1, baseShape: 'circle', baseA: 1, baseB: 1, height: 1.2 }],
  },
  frames: [{
    index: 0, seq: 1, kind: 'phase_started', round: 0, phase: 'deployment', line: 'Phase: deployment',
    models: [{ id: 'm1', where: 'shelf', wounds: 0, spellTokens: 0, conditions: [] }],
    units: [{ id: 'u1', actions: [], conditions: [], grants: [], woundsThisActivation: 0 }],
    markers: [], openAsks: [], rolls: [],
  }],
});

describe('readRecording', () => {
  it('accepts a well-formed recording and hands back exactly what it was given', () => {
    const r = readRecording(minimal());
    expect(r.frames).toHaveLength(1);
    expect(r.board.models[0]!.baseShape).toBe('circle');
    expect(teamOrdinals(r).get('t1')).toBe(1);
  });

  it('refuses a schemaVersion it does not know rather than mis-drawing it', () => {
    expect(() => readRecording({ ...minimal(), schemaVersion: 2 }))
      .toThrow(new RecordingError('schemaVersion 2 is not supported; this viewer reads 1. Re-run sim/ to get a current recording.'));
    expect(() => readRecording({ ...minimal(), schemaVersion: undefined })).toThrow(RecordingError);
  });

  it('refuses anything that is not an object with meta, board and frames', () => {
    for (const bad of [null, 42, 'x', [], {}, { schemaVersion: 1 }]) expect(() => readRecording(bad)).toThrow(RecordingError);
    const noBoard: Record<string, unknown> = minimal();
    delete noBoard.board;
    expect(() => readRecording(noBoard)).toThrow(/board/);
  });

  it('refuses frames whose index is not its position', () => {
    const r = minimal();
    r.frames = [{ ...r.frames[0]!, index: 3 }];
    expect(() => readRecording(r)).toThrow(/frame 0 carries index 3/);
  });

  it('refuses a frame that names a model the board does not define', () => {
    const r = minimal();
    r.frames[0]!.models = [{ id: 'ghost', where: 'shelf', wounds: 0, spellTokens: 0, conditions: [] }];
    expect(() => readRecording(r)).toThrow(/ghost/);
  });

  it('accepts a recording with no frames — a run that emitted nothing is still a run', () => {
    expect(readRecording({ ...minimal(), frames: [] }).frames).toEqual([]);
  });

  it('parses text and reports the JSON error as a RecordingError', () => {
    expect(parseRecording(JSON.stringify(minimal())).meta.scenario).toBe('terrain');
    expect(() => parseRecording('{ not json')).toThrow(RecordingError);
  });
});
