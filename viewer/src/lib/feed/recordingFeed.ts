// viewer/src/lib/feed/recordingFeed.ts
import type { RecordingSource } from '../recording/source';
import { Emitter, IDLE, type ControlChannel, type Feed, type FeedState } from './feed';

export { IDLE } from './feed';
export type { FeedState } from './feed';

/** A recording arrives complete, so this feed emits once and never again. */
export function recordingFeed(source: RecordingSource): Feed {
  const emitter = new Emitter({
    ...IDLE,
    label: source.label,
    status: 'open',
    meta: source.recording.meta,
    board: source.recording.board,
    frames: source.recording.frames,
  });

  return {
    kind: 'recording',
    control: null as ControlChannel | null,
    subscribe: (listener: (state: FeedState) => void) => emitter.subscribe(listener),
    close: () => emitter.emit({ status: 'closed' }),
  };
}
