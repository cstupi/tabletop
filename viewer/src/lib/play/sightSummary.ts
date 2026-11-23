// viewer/src/lib/play/sightSummary.ts
// client/src/Core/SightSummary.cs: the sight check drawn from the server's answer (in-game UX spec → Sight check): the summary over the
// target and the models dimmed under a target pick. It counts lines and reads canShoot and ranges; whether a unit is in cover is the rules' and is
// never decided here.

import type { Projection } from '../live/apply';
import type { AskOption, SightAnswer } from '../protocol/wire.generated';

/** "4 of 5 see Red Warriors · 2 screened" (the models that see it, then the clear lines a model screens), and while targets are declared how many can shoot and each range
 * a rule changed against this target: "· 1 can shoot · Carbine 18" → 14" (Shadowborn)". */
export function summaryText(answer: SightAnswer, target: string, shooting: boolean): string {
  const observers = new Set(answer.lines.map((l) => l.fromModelId)).size;
  const seeing = new Set(answer.lines.filter((l) => l.clear).map((l) => l.fromModelId)).size;
  const clear = answer.lines.filter((l) => l.clear).length;
  let text = `${seeing} of ${observers} see ${target}`;
  if (clear > 0) text += ` · ${answer.lines.filter((l) => l.clear && l.screened).length} screened`;
  if (!shooting) return text;
  text += ` · ${answer.canShoot.length} can shoot`;
  const changed = (answer.ranges ?? []).filter((r) => r.inches !== r.printed);
  if (changed.length === 0) return text;
  text += ' · ' + changed.map((r) => `${r.weapon} ${inches(r.printed)}" → ${inches(r.inches)}"`).join(', ');
  const rules = [...new Set(changed.flatMap((r) => r.by))];
  if (rules.length > 0) text += ` (${rules.join(', ')})`;
  return text;
}

/** One decimal at most, none when whole: C#'s "0.#". */
function inches(v: number): string {
  return String(Math.round(v * 10) / 10);
}

/**
 * The models to dim: the observers that cannot shoot the hovered target, and each assigned group's shooters that cannot shoot the group's
 * target. A group's shooters carry one of its weapons (the group's pieces are weapon ids). A target with no answer yet dims nothing.
 */
export function dimmed(
  p: Projection, hovered: SightAnswer | null, groups: readonly AskOption[], assigned: ReadonlyMap<string, string>, answers: ReadonlyMap<string, SightAnswer>,
): Set<string> {
  const dim = new Set<string>();
  if (hovered !== null) for (const l of hovered.lines) if (!hovered.canShoot.includes(l.fromModelId)) dim.add(l.fromModelId);
  for (const group of groups) {
    const target = assigned.get(group.id);
    const answer = target === undefined ? undefined : answers.get(target);
    if (answer === undefined) continue;
    for (const m of p.models.values())
      if (m.slot.weapons.some((w) => group.pieces.includes(w.id)) && !answer.canShoot.includes(m.id)) dim.add(m.id);
  }
  return dim;
}
