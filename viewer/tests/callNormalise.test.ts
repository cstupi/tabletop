// The same three literal cases are in tests/Tabletop.Client.Tests/CallNormaliseTests.cs.
import { describe, expect, it } from 'vitest';
import { canonical, knownIds, normaliseCall } from './callNormalise';

const run = (input: string, ...known: string[]): string => canonical(normaliseCall(JSON.parse(input), new Set(known)));

describe('call normalisation', () => {
  it('Nulls_go_numbers_round_keys_sort', () => {
    expect(run('{"kind":"add_setup_marker","marker":{"id":"019a0000-0000-7000-8000-00000000000f","kind":"objective","position":{"x":0.30000000000000004,"y":-0.0,"z":3},"yaw":{"degrees":0},"diameter":3,"label":null,"visual":{"builtin":"objective-ring"}}}'))
      .toBe('{"kind":"add_setup_marker","marker":{"diameter":3,"id":"new:1","kind":"objective","position":{"x":0.3,"y":0,"z":3},"visual":{"builtin":"objective-ring"},"yaw":{"degrees":0}}}');
  });

  it('Fresh_ids_are_numbered_in_canonical_order', () => {
    expect(run('{"z":"019a0000-0000-7000-8000-0000000000aa","a":["019a0000-0000-7000-8000-0000000000bb","019a0000-0000-7000-8000-0000000000aa"]}'))
      .toBe('{"a":["new:1","new:2"],"z":"new:2"}');
  });

  it('Known_ids_stay_and_keys_are_checked_too', () => {
    expect(run('{"counts":{"019a0000-0000-7000-8000-000000000001":2},"kind":"allocated"}', '019a0000-0000-7000-8000-000000000001'))
      .toBe('{"counts":{"019a0000-0000-7000-8000-000000000001":2},"kind":"allocated"}');
  });

  it('KnownIds_finds_every_guid_in_the_texts', () => {
    const ids = knownIds('{"a":"019A0000-0000-7000-8000-000000000001","b":"019a0000-0000-7000-8000-000000000002"}', '["019a0000-0000-7000-8000-000000000003"]');
    expect(ids.size).toBe(3);
    expect(ids.has('019a0000-0000-7000-8000-000000000001')).toBe(true);
  });
});
