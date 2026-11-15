// viewer/src/lib/play/workedLines.ts
// client/src/Core/WorkedLines.cs: the attack's units and weapon profiles, which logSheet.ts draws under a detailed attack's head.

import type { SequenceContext, Weapon } from '../protocol/wire.generated';
import type { Names } from './names';
import { RollWords } from './rollEntry';

/** WorkedLines.Attack: `Attack: {unit} ({n} models) → {targets}`, then one line per weapon name: `{name} ×{count} ({stats}, {rules})`. */
export function attackLines(c: SequenceContext, n: Names): string[] {
  const weapons = n.weaponsOf(c.weaponIds);
  const models = new Set(weapons.map((w) => w.modelId)).size;
  const who = c.actorUnitId !== undefined ? n.unit(c.actorUnitId) : 'Attack';
  const head = `Attack: ${who}${models > 0 ? ` (${RollWords.plural(models, 'model')})` : ''}`
    + (c.targetUnitIds.length > 0 ? ` → ${c.targetUnitIds.map((u) => n.unit(u)).join(', ')}` : '');
  const groups = new Map<string, Weapon[]>();
  for (const w of weapons) { const g = groups.get(w.weapon.name) ?? []; g.push(w.weapon); groups.set(w.weapon.name, g); }
  return [head, ...[...groups].map(([name, ws]) => `  ${name} ×${ws.length}${profile(ws[0]!)}`)];
}

function profile(w: Weapon): string {
  const parts = [
    ...Object.entries(w.stats).map(([k, v]) => (k === 'Range' ? v : k === 'Attacks' ? `A${v}` : `${k} ${v}`)),
    ...w.properties.map((p) => (p.parameter !== undefined && p.parameter !== null ? `${p.name}(${p.parameter})` : p.name)),
  ];
  return parts.length === 0 ? '' : ` (${parts.join(', ')})`;
}
