// What a wait says, and when it shows.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { countText, revealAfter, SHOW_AFTER_MS, tableToolsText } from '../src/lib/components/loading';
import { waitText } from '../src/lib/game/header';
import type { ConnectionState } from '../src/lib/connection/gameConnection';

describe('countText', () => {
  it('counts when the total is known', () => {
    expect(countText('Loading table tools', { done: 3, total: 9 })).toBe('Loading table tools 3 of 9');
  });

  it('ends in an ellipsis without a count', () => {
    expect(countText('Loading table tools', null)).toBe('Loading table tools…');
    expect(countText('Loading table tools', { done: 0, total: 0 })).toBe('Loading table tools…');
  });

  it('table tools', () => {
    expect(tableToolsText({ done: 2, total: 5 })).toBe('Loading table tools 2 of 5');
    expect(tableToolsText(null)).toBe('Loading table tools…');
  });
});

describe('revealAfter', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  it('shows after the delay', () => {
    const show = vi.fn();
    revealAfter(SHOW_AFTER_MS, show);
    vi.advanceTimersByTime(SHOW_AFTER_MS - 1);
    expect(show).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(show).toHaveBeenCalledOnce();
  });

  it('a wait that ends first never shows', () => {
    const show = vi.fn();
    const cancel = revealAfter(SHOW_AFTER_MS, show);
    vi.advanceTimersByTime(150);
    cancel();
    vi.advanceTimersByTime(1000);
    expect(show).not.toHaveBeenCalled();
  });

  it('no delay shows at once', () => {
    const show = vi.fn();
    revealAfter(0, show);
    expect(show).toHaveBeenCalledOnce();
  });

  it('the delay is 200 ms', () => { expect(SHOW_AFTER_MS).toBe(200); });
});

describe('waitText', () => {
  const snap = (phase: ConnectionState['phase'], welcomed: boolean) =>
    ({ phase, game: welcomed ? { name: 'G' } : null } as unknown as ConnectionState);

  it('before a socket', () => { expect(waitText(null)).toBe('Connecting…'); });
  it('connecting', () => { expect(waitText(snap('connecting', false))).toBe('Connecting…'); });
  it('reconnecting', () => { expect(waitText(snap('reconnecting', true))).toBe('Reconnecting…'); });
  it('welcomed, nothing applied yet', () => { expect(waitText(snap('connected', true))).toBe('Loading game…'); });
  it('connected before the welcome', () => { expect(waitText(snap('connected', false))).toBe('Connecting…'); });
});
