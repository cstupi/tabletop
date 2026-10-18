import { describe, expect, it } from 'vitest';
import { clear, click, keepOnly, NONE, selectAll } from '../src/lib/library/selection';

const order = ['a', 'b', 'c', 'd', 'e'];
const ids = (s: { ids: Set<string> }) => [...s.ids].sort();

describe('selection', () => {
  it('toggles one tile and moves the anchor to it', () => {
    let s = click(NONE, order, 'b', {});
    expect(ids(s)).toEqual(['b']); expect(s.anchor).toBe('b');
    s = click(s, order, 'd', {});
    expect(ids(s)).toEqual(['b', 'd']);
    s = click(s, order, 'b', {});
    expect(ids(s)).toEqual(['d']); expect(s.anchor).toBe('b');
  });
  it('selects the range from the anchor in either direction and keeps what was selected', () => {
    let s = click(click(NONE, order, 'a', {}), order, 'd', {});    // a, d; anchor d
    s = click(s, order, 'b', { range: true });
    expect(ids(s)).toEqual(['a', 'b', 'c', 'd']); expect(s.anchor).toBe('d');
    s = click(click(NONE, order, 'b', {}), order, 'e', { range: true });
    expect(ids(s)).toEqual(['b', 'c', 'd', 'e']);
  });
  it('treats a range click without an anchor in view as a toggle', () => {
    expect(ids(click(NONE, order, 'c', { range: true }))).toEqual(['c']);
    expect(ids(click({ ids: new Set(['z']), anchor: 'z' }, order, 'c', { range: true }))).toEqual(['c', 'z']);
  });
  it('selects all in view, adding to the selection, and clears', () => {
    const s = selectAll({ ids: new Set(['z']), anchor: null }, ['a', 'b']);
    expect(ids(s)).toEqual(['a', 'b', 'z']);
    expect(clear().ids.size).toBe(0);
  });
  it('drops ids that left the view and keeps the same object when nothing left', () => {
    const s = click(click(NONE, order, 'a', {}), order, 'c', {});
    expect(keepOnly(s, ['a', 'b'])).toEqual({ ids: new Set(['a']), anchor: null });
    expect(keepOnly(s, order)).toBe(s);
  });
});
