// viewer/tests/fixture.ts — the four frames a live socket delivers on connect, plus the recording of
// the same run. Written by tests/Tabletop.Viewer.Tests/FixtureWriter.cs and checked in.
import { readFileSync } from 'node:fs';
import { readRecording } from '../src/lib/recording/read';
import type { Board, GameEvent, GameInfo, SocketMessage } from '../src/lib/protocol/wire.generated';
import type { Recording } from '../src/lib/protocol/recording.generated';

export const FIXTURES = ['skirmish', 'terrain'] as const;

const read = (name: string): unknown => JSON.parse(readFileSync(`tests/fixtures/${name}.json`, 'utf8'));

export function loadFixture(name: string): {
  game: GameInfo; board: Board; events: readonly GameEvent[]; recording: Recording;
  frames: readonly SocketMessage[];
} {
  const welcome = read(`${name}.welcome`) as SocketMessage;
  const board = read(`${name}.board`) as SocketMessage;
  const events = read(`${name}.events`) as SocketMessage;
  if (welcome.kind !== 'welcome' || board.kind !== 'board' || events.kind !== 'events')
    throw new Error(`${name}: the fixture frames are not welcome, board and events`);
  return {
    game: welcome.game,
    board: board.board,
    events: events.events,
    recording: readRecording(read(`${name}.recording`)),
    frames: [welcome, board, events],
  };
}
