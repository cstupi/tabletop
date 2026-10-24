// web/src/lib/replays.ts — where the header's Play link points: the viewer's hosted build, same origin under /viewer/
// (web/Dockerfile), or PUBLIC_VIEWER_URL (the dev viewer on :5174 under compose).
export const REPLAYS_URL: string = (import.meta.env?.PUBLIC_VIEWER_URL as string | undefined) || '/viewer/';
