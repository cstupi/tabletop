import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import type { EventPayload, GameEvent } from '../src/lib/protocol/wire.generated';
import { ReplayClock, SPEEDS, STEP_SECONDS } from '../src/lib/replay/replayClock';
import { ReplayPlayer } from '../src/lib/replay/replayPlayer';
import { ReplayTimeline, replayStops } from '../src/lib/replay/replayTimeline';
import { EMPTY, reduce } from '../src/lib/timeline/timeline';
import { loadFixture } from './fixture';

const ev = (seq: number, payload: Partial<EventPayload> & { kind: string }, actor?: string): GameEvent =>
  ({ seq, payload: payload as EventPayload, ...(actor === undefined ? {} : { actor }) });

describe('replayStops (ReplayTimeline.Stops)', () => {
  it('steps before each run of one participant\'s events, plus 0 and the end', () => {
    const log = [ev(1, { kind: 'phase_started' }), ev(2, { kind: 'ask_answered' }, 'a'), ev(3, { kind: 'step_completed' }, 'a'),
      ev(4, { kind: 'step_completed' }), ev(5, { kind: 'ask_answered' }, 'b'), ev(6, { kind: 'ask_closed' })];
    expect(replayStops(log, 'step')).toEqual([0, 1, 4, 6]);
  });

  it('stops just after each turn_started and round_started', () => {
    const log = [ev(1, { kind: 'round_started' }), ev(2, { kind: 'turn_started' }), ev(3, { kind: 'turn_ended' }), ev(4, { kind: 'turn_started' })];
    expect(replayStops(log, 'round')).toEqual([0, 1, 4]);
    expect(replayStops(log, 'turn')).toEqual([0, 2, 4]);
  });

  it('stops just after each action frame ends; nested frames and an undo past the open one are handled', () => {
    const start = (seq: number, frameId: string, action?: string) => ev(seq, { kind: 'sequence_started', frameId, context: action === undefined ? {} : { action } } as never);
    const log = [
      start(1, 'f1', 'Hold'), start(2, 'n1'), ev(3, { kind: 'sequence_finished', frameId: 'n1' } as never), ev(4, { kind: 'sequence_finished', frameId: 'f1' } as never),
      start(5, 'f2', 'Advance'), ev(6, { kind: 'undone', toSeq: 4 } as never), start(7, 'f3', 'Rush'), ev(8, { kind: 'sequence_finished', frameId: 'f2' } as never),
      ev(9, { kind: 'sequence_finished', frameId: 'f3' } as never), ev(10, { kind: 'turn_ended' }),
    ];
    expect(replayStops(log, 'action')).toEqual([0, 4, 9, 10]);
  });

  it('runs over a recorded game: every unit ascends from 0 to the whole log', () => {
    const { events } = loadFixture('skirmish');
    for (const unit of ['step', 'action', 'turn', 'round'] as const) {
      const stops = replayStops(events, unit);
      expect(stops[0]).toBe(0);
      expect(stops.at(-1)).toBe(events.length);
      expect([...stops].sort((a, b) => a - b)).toEqual(stops);
    }
    expect(replayStops(events, 'round')).toHaveLength(3 + 2);
  });
});

describe('ReplayTimeline', () => {
  const { board, game, events } = loadFixture('skirmish');

  it('starts at 0 with the board as setup left it, and seeks forward and back to the same projection as applying the log', () => {
    const t = new ReplayTimeline(board, game, () => events);
    expect(t.position).toBe(0);
    expect(t.count).toBe(events.length);
    expect(t.caption).toBe(`Before round 1 · 0 of ${events.length}`);
    expect(t.seek(50)).toHaveLength(50);
    const direct = new Applier(board, game);
    direct.applyAll(events.slice(0, 30));
    expect(t.seek(30)).toBeNull();
    expect(t.position).toBe(30);
    expect(t.shown).toEqual(events.slice(0, 30));
    expect(t.projection.lastSeq).toBe(direct.projection.lastSeq);
    expect([...t.projection.models.values()]).toEqual([...direct.projection.models.values()]);
  });

  it('clamps, steps by round and names the round in the caption', () => {
    const t = new ReplayTimeline(board, game, () => events);
    t.seek(9999);
    expect(t.atEnd).toBe(true);
    t.seek(0);
    t.seek(t.next('round'));                       // round 0, deployment
    expect(t.caption).toBe('Before round 1 · 8 of 225');
    t.seek(t.next('round'));
    expect(t.projection.scheduler.round).toBe(1);
    expect(t.caption).toBe('Round 1 · 47 of 225');
    expect(t.previous('round')).toBe(8);
  });

  it('a growing log extends the timeline and leaves the position', () => {
    let log = events.slice(0, 20);
    const t = new ReplayTimeline(board, game, () => log);
    t.seek(20);
    log = events.slice(0, 40);
    expect(t.count).toBe(40);
    expect(t.position).toBe(20);
    expect(t.atEnd).toBe(false);
  });
});

describe('ReplayClock', () => {
  it('is due every STEP_SECONDS over the speed while playing', () => {
    const c = new ReplayClock();
    expect(SPEEDS).toEqual([0.5, 1, 2, 4, 8]);
    expect(c.tick(10)).toBe(false);
    c.play();
    expect(c.tick(STEP_SECONDS - 0.1)).toBe(false);
    expect(c.tick(0.1)).toBe(true);
    c.speed = 4;
    expect(c.tick(STEP_SECONDS / 4)).toBe(true);
    c.pause();
    expect(c.tick(10)).toBe(false);
  });
});

describe('ReplayPlayer', () => {
  const { board, game, events } = loadFixture('skirmish');
  const playerOf = (ended = true) => new ReplayPlayer(new ReplayTimeline(board, game, () => events), () => ended);

  it('plays: Play moves forward one step each STEP_SECONDS, and Pause stops it', () => {
    const p = playerOf();
    let changes = 0;
    p.subscribe(() => { changes++; });
    p.togglePlay();
    expect(p.playing).toBe(true);
    p.advance(STEP_SECONDS);
    const first = p.timeline.position;
    expect(first).toBe(replayStops(events, 'step')[1]);
    p.advance(STEP_SECONDS);
    expect(p.timeline.position).toBeGreaterThan(first);
    p.togglePlay();
    expect(p.playing).toBe(false);
    const held = p.timeline.position;
    p.advance(60);
    expect(p.timeline.position).toBe(held);
    expect(changes).toBeGreaterThanOrEqual(4);
  });

  it('pauses at the end of an ended game, and Play there starts again from the start', () => {
    const p = playerOf();
    p.seek(events.length);
    p.togglePlay();
    expect(p.timeline.position).toBe(0);
    for (let i = 0; i < 1000 && p.playing; i++) p.advance(STEP_SECONDS);
    expect(p.playing).toBe(false);
    expect(p.timeline.atEnd).toBe(true);
  });

  it('waits at the end of a running game for the next events', () => {
    const p = playerOf(false);
    p.seek(events.length);
    p.togglePlay();
    expect(p.timeline.position).toBe(events.length);
    p.advance(STEP_SECONDS);
    expect(p.playing).toBe(true);
  });

  it('a step button pauses and jumps to the next or previous stop', () => {
    const p = playerOf();
    p.togglePlay();
    p.step('turn', false);
    expect(p.playing).toBe(false);
    expect(p.timeline.position).toBe(replayStops(events, 'turn')[1]);
    p.step('turn', true);
    expect(p.timeline.position).toBe(0);
    p.speed = 8;
    expect(p.bar).toMatchObject({ position: 0, count: events.length, playing: false, speed: 8 });
  });
});

describe('the Watch timeline\'s Play', () => {
  it('opened at the last frame (a live feed follows the end), Play starts again from the first frame and keeps playing', () => {
    let t = reduce({ ...EMPTY, follow: true }, { type: 'frames', count: 5 });
    expect(t.index).toBe(4);
    t = reduce(t, { type: 'play' });
    expect(t).toMatchObject({ index: 0, playing: true, follow: false });
    t = reduce(t, { type: 'tick' });
    expect(t).toMatchObject({ index: 1, playing: true });
    t = reduce(t, { type: 'frames', count: 6 });
    expect(t.index).toBe(1);
  });
});
