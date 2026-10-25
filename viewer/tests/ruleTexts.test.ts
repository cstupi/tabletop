import { describe, expect, it, vi } from 'vitest';
import type { Api } from '../src/lib/api/client';
import { fetchRuleTexts, grantChoices, RuleTexts, type GrantableEffectDto, type RuleTextDto } from '../src/lib/play/ruleTexts';

const tough: RuleTextDto = { name: 'Tough', kind: 'unit', takesParameter: true, ruleText: 'Must take X wounds to be killed.' };
const ambush: RuleTextDto = { name: 'Ambush', kind: 'unit', takesParameter: false, ruleText: 'May be set aside before deployment.' };

describe('RuleTexts', () => {
  it('a catalog entry gives its text and reads as automated', () => {
    const rules = RuleTexts.from([tough, ambush]);
    expect(rules.textOf('Tough')).toBe('Must take X wounds to be killed.');
    expect(rules.isAutomated('Ambush')).toBe(true);
  });

  it('a name with no entry has no text and is not automated', () => {
    const rules = RuleTexts.from([tough]);
    expect(rules.textOf('Furious')).toBeNull();
    expect(rules.isAutomated('Furious')).toBe(false);
    expect(rules.notAutomated('Furious')).toBe(true);
    expect(rules.notAutomated('Tough')).toBe(false);
  });

  it('names match as the catalog writes them', () => {
    const rules = RuleTexts.from([tough]);
    expect(rules.textOf('tough')).toBeNull();
    expect(rules.textOf('Tough(3)')).toBeNull();
  });

  it('empty knows no rule', () => {
    expect(RuleTexts.empty.textOf('Tough')).toBeNull();
    expect(RuleTexts.empty.isAutomated('Tough')).toBe(false);
    expect(RuleTexts.empty.loaded).toBe(false);
    expect(RuleTexts.empty.notAutomated('Tough')).toBe(false);
  });

  it('a repeated name keeps the first row', () => {
    expect(RuleTexts.from([tough, { ...tough, ruleText: 'other' }]).textOf('Tough')).toBe('Must take X wounds to be killed.');
  });

  it('an unplayed entry keeps its text and is not automated', () => {
    const rules = RuleTexts.from([tough, { name: 'Precise', kind: 'model', takesParameter: false, ruleText: 'Gets +1 to hit when attacking.', played: false }]);
    expect(rules.textOf('Precise')).toBe('Gets +1 to hit when attacking.');
    expect(rules.isAutomated('Precise')).toBe(false);
    expect(rules.notAutomated('Precise')).toBe(true);
    expect(rules.isAutomated('Tough')).toBe(true);
  });

  it('a row without played reads as played', () => {
    const row = JSON.parse('{"name":"Tough","kind":"model","takesParameter":true,"ruleText":"T."}') as RuleTextDto;
    expect(RuleTexts.from([row]).isAutomated('Tough')).toBe(true);
  });
});

const grantable: GrantableEffectDto[] = [
  { entryName: 'Growth', effectId: 'g', duration: 'marker' }, { entryName: 'Hex', effectId: 'a', duration: 'once' }, { entryName: 'Hex', effectId: 'b', duration: 'once' },
];

describe('grantChoices', () => {
  it('each effect is a choice named by its entry and numbered when the entry has several', () => {
    expect(grantChoices(grantable).map((c) => c.label)).toEqual(['Growth', 'Hex (1 of 2)', 'Hex (2 of 2)']);
    expect(grantChoices(grantable)[2]).toEqual({ label: 'Hex (2 of 2)', entryName: 'Hex', effectId: 'b' });
  });
});

describe('fetchRuleTexts', () => {
  const api = (rules: () => Promise<RuleTextDto[]>, grants: () => Promise<GrantableEffectDto[]>) => ({ ruleTexts: vi.fn(rules), grantableEffects: vi.fn(grants) }) as unknown as Api;

  it('returns null when the rules request fails and empty grants when only grants fail', async () => {
    expect(await fetchRuleTexts(api(() => Promise.reject(new Error('x')), async () => grantable), 'h')).toBeNull();
    const ok = await fetchRuleTexts(api(async () => [tough], () => Promise.reject(new Error('x'))), 'h');
    expect(ok!.rules.textOf('Tough')).toBe('Must take X wounds to be killed.');
    expect(ok!.grants).toEqual([]);
    const both = await fetchRuleTexts(api(async () => [tough], async () => grantable), 'h');
    expect(both!.grants).toHaveLength(3);
  });

  it('does not ask for grants when the rules failed', async () => {
    const a = api(() => Promise.reject(new Error('x')), async () => grantable);
    await fetchRuleTexts(a, 'h');
    expect((a as unknown as { grantableEffects: ReturnType<typeof vi.fn> }).grantableEffects).not.toHaveBeenCalled();
  });
});
