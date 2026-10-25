// viewer/src/lib/game/connectionFeed.ts
// A Feed over a GameConnection's frames, so PlaybackView draws a game in play until plan D's play page lands. It follows the
// connection: a new board rebuilds the projector, then each applied event is added once.

import type { GameConnection } from '../connection/gameConnection';
import type { RecordedTeam, RecordingMeta } from '../protocol/recording.generated';
import type { GameInfo } from '../protocol/wire.generated';
import { ProjectionDesyncError } from '../live/apply';
import { FrameProjector } from '../live/project';
import { Emitter, IDLE, type Feed } from '../feed/feed';

function metaOf(info: GameInfo, lastSeq: number, rounds: number, started: number): RecordingMeta {
  const teams: RecordedTeam[] = info.teams.map((team) => ({
    id: team.id, ordinal: team.ordinal, name: `Team ${team.ordinal}`,
    participants: info.participants.filter((p) => p.teamId === team.id).map((p) => p.name),
  }));
  return {
    scenario: info.name, seed: 0, driver: 'live', gameRules: info.gameRules, propertiesHash: info.propertiesHash,
    schedulerShape: info.schedulerShape, enforcement: info.enforcement, teams, outcome: 'live', rounds, lastSeq,
    startedAt: new Date(started).toISOString(), durationMs: Date.now() - started,
  };
}

export function connectionFeed(connection: GameConnection): Feed {
  const started = Date.now();
  const emitter = new Emitter({ ...IDLE, label: `game · ${connection.gameId}`, status: 'loading' });
  let board: unknown = null;
  let projector: FrameProjector | null = null;
  let added = 0;

  const unsubscribe = connection.subscribe((state) => {
    if (state.phase === 'closed') return emitter.emit({ status: 'closed' });
    if (state.game === null || state.board === null) return;
    try {
      if (state.board !== board) {
        board = state.board;
        projector = new FrameProjector(state.board, state.game);
        added = 0;
      }
      if (projector === null) return;
      for (; added < state.log.length; added++) projector.add(state.log[added]!);
    } catch (error) {
      projector = null;
      return emitter.emit({
        status: 'error',
        detail: error instanceof ProjectionDesyncError ? `desync: ${error.kind} names ${error.unknownId}` : String(error),
      });
    }
    const frames = projector.frames;
    const last = frames.length === 0 ? null : frames[frames.length - 1]!;
    emitter.emit({
      status: 'open', board: projector.board, frames: [...frames], meta: metaOf(state.game, last?.seq ?? 0, last?.round ?? 0, started),
    });
  });

  return {
    kind: 'live',
    control: null,
    subscribe: (listener) => emitter.subscribe(listener),
    close: () => { unsubscribe(); },
  };
}
