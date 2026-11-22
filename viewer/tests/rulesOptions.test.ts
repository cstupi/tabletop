import { describe, expect, it } from 'vitest';
import { CUSTOM, configurationOf, mapOf, presetOf } from '../src/lib/lobby/rulesOptions';
import type { GameInfo, RulesOptionCatalog } from '../src/lib/protocol/wire.generated';

const catalog: RulesOptionCatalog = {
  options: [
    { key: 'a', label: 'A', default: 'off', description: 'Option A.', values: [{ value: 'off', label: 'Off', description: 'A off.' }, { value: 'on', label: 'On', description: 'A on.' }] },
    { key: 'b', label: 'B', default: 'x', description: 'Option B.', values: [{ value: 'x', label: 'X', description: 'B x.' }, { value: 'y', label: 'Y', description: 'B y.' }] },
  ],
  presets: [{ name: 'Core', optionalRules: {} }, { name: 'Wild', optionalRules: { a: 'on', b: 'y' } }],
};

describe('RulesOptions', () => {
  it('presetOf finds the matching preset else Custom', () => {
    expect(presetOf(catalog, {})).toBe('Core');
    expect(presetOf(catalog, { a: 'on', b: 'y' })).toBe('Wild');
    expect(presetOf(catalog, { a: 'on' })).toBe(CUSTOM);
  });
  it('mapOf drops defaults', () => {
    expect(mapOf(catalog, { a: 'on', b: 'x' })).toEqual({ a: 'on' });
  });
  it('configurationOf sends the picked hash over the game\'s', () => {
    const game = { propertiesHash: 'hash' } as GameInfo;
    expect(configurationOf(game, 'N', 'player-turn', 'strict', catalog, {}, 'other').propertiesHash).toBe('other');
  });
  it('configurationOf carries the hash and the map', () => {
    const game = { propertiesHash: 'hash' } as GameInfo;
    expect(configurationOf(game, '  N ', 'player-turn', 'strict', catalog, { b: 'y' })).toEqual({
      kind: 'set_configuration', name: 'N', propertiesHash: 'hash', schedulerShape: 'player-turn', enforcement: 'strict',
      waitsBetweenSteps: false, optionalRules: { b: 'y' },
    });
  });
});
