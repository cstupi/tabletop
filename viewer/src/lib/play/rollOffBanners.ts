// viewer/src/lib/play/rollOffBanners.ts
// client/src/Core/RollOffBanners.cs: a step_completed whose values carry place_1..place_n (team ordinals, best first) and
// rolled_<ordinal>_<k> (each team's faces in order) is the roll-off's ranking; it shows "Team k goes first" with each team's roll under it.
// Live batches only: nothing while catching up or in a batch with an undo. The browser has no setting for the banner.

import type { GameEvent, StepCompleted, Team } from '../protocol/wire.generated';
import type { Names } from './names';

/** team: the ordinal whose band and icon lead the banner (0: none); parts: the second line per team, each in its colour (TurnBanners.Banner). */
export interface BannerPart { readonly team: number; readonly text: string }
export interface Banner { readonly text: string; readonly seconds: number; readonly detail: string | null; readonly team: number; readonly parts: readonly BannerPart[] }
export const SECONDS = 4;

const place = (k: number): string => `place_${k}`;

export function rollOffBannerOf(batch: readonly GameEvent[], n: Names, catchingUp: boolean): Banner | null {
  if (catchingUp || batch.some((e) => e.payload.kind === 'undone')) return null;
  const steps = batch.map((e) => e.payload).filter((x): x is StepCompleted => x.kind === 'step_completed' && x.values !== undefined && place(1) in x.values);
  const values = steps[steps.length - 1]?.values;
  if (values === undefined) return null;
  const placed: Team[] = [];
  for (let k = 1; place(k) in values; k++) {
    const team = n.teams.find((t) => t.ordinal === values[place(k)]);
    if (team !== undefined && !placed.includes(team)) placed.push(team);
  }
  if (placed.length === 0) return null;
  const rolls = placed.map((team) => ({ team, faces: faces(values, team.ordinal) })).filter((r) => r.faces.length > 0);
  const detail = rolls.length === 0 ? null : rolls.map((r) => `${n.team(r.team.id)} rolled ${r.faces.join(', ')}`).join(' · ');
  return { text: `${n.team(placed[0]!.id)} goes first`, seconds: SECONDS, detail, team: placed[0]!.ordinal,
    parts: rolls.map((r) => ({ team: r.team.ordinal, text: `${n.team(r.team.id)} rolled ${r.faces.join(', ')}` })) };
}

function faces(values: Readonly<Record<string, number>>, ordinal: number): number[] {
  const out: number[] = [];
  for (let k = 1; `rolled_${ordinal}_${k}` in values; k++) out.push(values[`rolled_${ordinal}_${k}`]!);
  return out;
}
