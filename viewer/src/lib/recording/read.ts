import { SCHEMA_VERSION, type Recording } from '../protocol/recording.generated';

/** A file this viewer will not draw. The fix is to re-run sim/, because a recording is derived. */
export class RecordingError extends Error {}

function object(value: unknown, what: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new RecordingError(`${what} is not an object`);
  return value as Record<string, unknown>;
}

function array(value: unknown, what: string): readonly unknown[] {
  if (!Array.isArray(value)) throw new RecordingError(`${what} is not an array`);
  return value;
}

export function readRecording(value: unknown): Recording {
  const root = object(value, 'the recording');
  if (root.schemaVersion !== SCHEMA_VERSION)
    throw new RecordingError(
      `schemaVersion ${root.schemaVersion} is not supported; this viewer reads ${SCHEMA_VERSION}. ` +
        `Re-run sim/ to get a current recording.`,
    );

  const meta = object(root.meta, 'meta');
  array(meta.teams, 'meta.teams');
  const board = object(root.board, 'board');
  const models = array(board.models, 'board.models');
  array(board.terrain, 'board.terrain');
  array(board.markers, 'board.markers');
  array(board.zones, 'board.zones');
  if (typeof board.width !== 'number' || typeof board.length !== 'number')
    throw new RecordingError('board.width and board.length must be numbers');

  const known = new Set(models.map((m) => object(m, 'a board model').id as string));
  const frames = array(root.frames, 'frames');
  frames.forEach((raw, i) => {
    const frame = object(raw, `frame ${i}`);
    if (frame.index !== i) throw new RecordingError(`frame ${i} carries index ${frame.index}`);
    if (typeof frame.seq !== 'number') throw new RecordingError(`frame ${i} has no seq`);
    if (typeof frame.line !== 'string') throw new RecordingError(`frame ${i} has no line`);
    array(frame.units, `frame ${i}.units`);
    array(frame.markers, `frame ${i}.markers`);
    array(frame.openAsks, `frame ${i}.openAsks`);
    array(frame.rolls, `frame ${i}.rolls`);
    for (const m of array(frame.models, `frame ${i}.models`)) {
      const id = object(m, `a model in frame ${i}`).id as string;
      if (!known.has(id)) throw new RecordingError(`frame ${i} names model ${id}, which board.models does not define`);
    }
  });

  return value as Recording;
}

export function parseRecording(text: string): Recording {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (cause) {
    throw new RecordingError(`not JSON: ${(cause as Error).message}`);
  }
  return readRecording(parsed);
}

export function teamOrdinals(r: Recording): ReadonlyMap<string, number> {
  return new Map(r.meta.teams.map((t) => [t.id, t.ordinal]));
}

export function teamNames(r: Recording): ReadonlyMap<string, string> {
  return new Map(r.meta.teams.map((t) => [t.id, t.name]));
}
