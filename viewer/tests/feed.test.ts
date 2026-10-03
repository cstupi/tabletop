import { describe, expect, it } from 'vitest';
import { IDLE, recordingFeed } from '../src/lib/feed/recordingFeed';
import type { FeedState } from '../src/lib/feed/feed';
import type { Recording } from '../src/lib/protocol/recording.generated';
import { SCHEMA_VERSION } from '../src/lib/protocol/recording.generated';

const recording = {
  schemaVersion: SCHEMA_VERSION,
  meta: { scenario: 'skirmish', seed: 3, driver: 'in-process', gameRules: 'grimdark-future',
    propertiesHash: 'sha256:x', schedulerShape: 'alternating-activation', enforcement: 'strict',
    teams: [], outcome: 'ended', rounds: 2, lastSeq: 2, startedAt: '1970-01-01T00:00:00+00:00', durationMs: 4 },
  board: { width: 72, length: 48, texture: 'grass', terrain: [], markers: [], zones: [], models: [] },
  frames: [
    { index: 0, seq: 1, kind: 'phase_started', round: 0, line: 'Phase: deployment', models: [], units: [], markers: [], openAsks: [], rolls: [] },
    { index: 1, seq: 2, kind: 'round_started', round: 1, line: 'Round 1 started', models: [], units: [], markers: [], openAsks: [], rolls: [] },
  ],
} as unknown as Recording;

describe('the recording feed', () => {
  it('starts idle with nothing to draw', () => {
    expect(IDLE).toEqual({ label: '', status: 'idle', detail: null, meta: null, board: null, frames: [] });
  });

  it('hands a subscriber the whole recording at once and stays open', () => {
    const seen: FeedState[] = [];
    const feed = recordingFeed({ label: 'run.json', recording });
    feed.subscribe((s) => seen.push(s));
    expect(seen).toHaveLength(1);
    expect(seen[0]).toMatchObject({ label: 'run.json', status: 'open', frames: recording.frames });
    expect(seen[0]!.board!.width).toBe(72);
    expect(feed.kind).toBe('recording');
  });

  it('leaves the control channel empty — the viewer sends nothing', () => {
    expect(recordingFeed({ label: 'run.json', recording }).control).toBeNull();
  });

  it('stops emitting once unsubscribed and once closed', () => {
    const feed = recordingFeed({ label: 'run.json', recording });
    const seen: FeedState[] = [];
    const stop = feed.subscribe((s) => seen.push(s));
    stop();
    feed.close();
    expect(seen).toHaveLength(1);
    const after: FeedState[] = [];
    feed.subscribe((s) => after.push(s));
    expect(after[0]!.status).toBe('closed');
  });
});
