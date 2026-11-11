// web/src/lib/download/platforms.ts — which build the Download page offers first, and what an unsigned build asks of a
// player on first run (client releases spec → Serving).
import type { ClientDownload, ClientPlatform } from '$lib/api/types';

export const PLATFORM_NAMES: Record<ClientPlatform, string> = { windows: 'Windows', macos: 'macOS', linux: 'Linux' };
export const PLATFORM_DETAIL: Record<ClientPlatform, string> = { windows: '64-bit (x86-64)', macos: 'Apple silicon and Intel', linux: '64-bit (x86-64)' };

/** The visitor's OS from `navigator.userAgentData.platform` when the browser gives it, else the user agent. Phones and
 *  tablets get none: there is no build for them. */
export function detectPlatform(userAgent: string, platformHint?: string): ClientPlatform | null {
  const hint = (platformHint ?? '').toLowerCase();
  if (hint === 'windows') return 'windows';
  if (hint === 'macos') return 'macos';
  if (hint === 'linux') return 'linux';
  if (['chrome os', 'chromeos', 'android', 'ios'].includes(hint)) return null;
  const ua = userAgent.toLowerCase();
  if (/android|iphone|ipad|ipod|mobile|cros/.test(ua)) return null;
  if (ua.includes('windows')) return 'windows';
  if (ua.includes('mac os x') || ua.includes('macintosh')) return 'macos';
  if (ua.includes('linux') || ua.includes('x11')) return 'linux';
  return null;
}

/** The detected platform's build first, the rest in the server's order. */
export function orderDownloads(downloads: ClientDownload[], detected: ClientPlatform | null): ClientDownload[] {
  return [...downloads].sort((a, b) => Number(b.platform === detected) - Number(a.platform === detected));
}

/** First-run steps for an unsigned build, per platform. */
export const FIRST_RUN: Record<ClientPlatform, string[]> = {
  windows: [
    'Extract the zip to a folder of its own, then run Tabletop.exe.',
    'The build is not signed yet, so Windows SmartScreen may say it protected your PC: choose More info, then Run anyway.',
  ],
  macos: [
    'Open the zip; move Tabletop.app and Open Tabletop.command to Applications. Needs macOS 12 or later.',
    'The build is not signed or notarised yet, so macOS blocks it. Double-click Open Tabletop.command; when macOS refuses it, open System Settings → Privacy & Security, choose Open Anyway beside it, then double-click it again.',
    'It unblocks Tabletop and starts it. From then on, open Tabletop.app.',
  ],
  linux: [
    'Extract the zip to a folder of its own.',
    'If the file manager lost the executable bit: chmod +x Tabletop.x86_64, then run ./Tabletop.x86_64.',
  ],
};
