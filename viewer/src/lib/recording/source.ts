import type { Recording } from '../protocol/recording.generated';
import { RecordingError, parseRecording } from './read';

export type RecordingSource = { readonly label: string; readonly recording: Recording };

export async function fromFile(file: File): Promise<RecordingSource> {
  return { label: file.name, recording: parseRecording(await file.text()) };
}

export async function fromUrl(url: string): Promise<RecordingSource> {
  const response = await fetch(url);
  if (!response.ok) throw new RecordingError(`${url} answered ${response.status} ${response.statusText}`);
  return { label: url, recording: parseRecording(await response.text()) };
}

/** `?recording=<url>` — the whole of the viewer's routing (Task 1: no router). */
export function recordingUrlFrom(search: string): string | null {
  return new URLSearchParams(search).get('recording');
}
