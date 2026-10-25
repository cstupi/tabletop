// viewer/src/lib/play/deployView.ts
// client/src/Core/DeployView.cs: deploying from the shelf. The deploy Asks pick a unit, and the activation ends only when every model of
// it is placed (scheduler → Deployment). `layout` and `around` serve D2's deploy placement.

import type { ModelInPlay, Projection } from '../live/apply';
import { effectiveLog } from '../live/apply';
import type { Ask, Base, GameEvent, ModelRelocation, Placement, Point3, TableSize } from '../protocol/wire.generated';

/** A deploy activation the server is waiting on: the unit picked at a deploy Ask, and the models of it still on the shelf. */
export interface MidDeploy { readonly unitId: string; readonly question: string; readonly shelvedModelIds: readonly string[] }

/** deploy, deploy_scouts, deploy_ambusher, deploy_reinforcement. */
export const DEPLOY_QUESTIONS: readonly string[] = ['deploy', 'deploy_scouts', 'deploy_ambusher', 'deploy_reinforcement'];
const PER_ROW = 5, MAX_RINGS = 64, GAP = 0.2;

/**
 * The unit my team picked at the latest Ask, when that Ask is a deploy question that has closed with a unit and some of its models are
 * still shelved. Any later Ask — the next deploy, an activation — or a turn's end ends it, because the server moved on (a pick withdrawn
 * when its unit was set aside ends the turn when the team has nothing left to deploy). Either player of the team places it (rulebook p.50).
 */
export function midDeploy(p: Projection, log: readonly GameEvent[], myTeam: string | null): MidDeploy | null {
  if (myTeam === null) return null;
  let last: Ask | null = null;
  let ended = false;
  const live = effectiveLog(log);                                // an undo takes back the Asks, closes and turn ends after its point
  for (const e of live) {
    if (e.payload.kind === 'ask_opened') { last = e.payload.ask; ended = false; }
    else if (e.payload.kind === 'turn_ended') ended = true;
  }
  if (last === null || ended || !DEPLOY_QUESTIONS.includes(last.question) || !last.teams.includes(myTeam) || p.openAsks.has(last.id)) return null;
  let answer;
  for (let i = live.length - 1; i >= 0; i--) {
    const c = live[i]!.payload;
    if (c.kind === 'ask_closed' && c.askId === last.id) { answer = c.answers.find((a) => a.teamId === myTeam)?.answer; break; }
  }
  if (answer?.kind !== 'selected_pieces' || answer.pieceIds.length !== 1) return null;
  const unit = answer.pieceIds[0]!;
  const shelved = [...p.models.values()].filter((m) => m.unitId === unit && m.location.kind === 'shelf')
    .sort((a, b) => a.slot.ordinal - b.slot.ordinal).map((m) => m.id);
  return shelved.length === 0 ? null : { unitId: unit, question: last.question, shelvedModelIds: shelved };
}

const fit = (centre: number, half: number, extent: number): number => (extent <= 2 * half ? extent / 2 : Math.min(Math.max(centre, half), extent - half));
const widest = (models: readonly ModelInPlay[]): number => Math.max(...models.map((m) => Math.max(m.slot.base.a, m.slot.base.b)));

/**
 * One relocation per model to a tight grid at yaw 0 — rows of five, spaced by the widest base plus a small gap — centred on the point and
 * pushed in so the whole grid is on the table. The player drags the models into position afterwards.
 */
export function layout(models: readonly ModelInPlay[], at: Point3, size: TableSize): ModelRelocation[] {
  if (models.length === 0) return [];
  const pitch = widest(models) + GAP;
  const cols = Math.min(models.length, PER_ROW);
  const rows = Math.floor((models.length + cols - 1) / cols);
  const cx = fit(at.x, cols * pitch / 2, size.width);
  const cy = fit(at.y, rows * pitch / 2, size.length);
  return models.map((m, i): ModelRelocation => ({
    modelId: m.id, from: m.location,
    to: { kind: 'table', placement: {
      position: { x: cx + ((i % cols) - (cols - 1) / 2) * pitch, y: cy - (Math.floor(i / cols) - (rows - 1) / 2) * pitch, z: at.z },
      yaw: { degrees: 0 } } },
  }));
}

/**
 * Shelf models placed relative to a model on the table (a Spawn or Split reserve, placed "fully within 6\"" of its anchor): rings about the
 * anchor's centre, the first clear of its base by the separation, each as many models as fit a pitch apart, yaw 0; a spot whose base would
 * leave the table is skipped. A row of `layout` would reach past 6" for five wide bases; a ring keeps every model as close as it can go.
 */
export function around(models: readonly ModelInPlay[], anchor: Placement, anchorBase: Base, size: TableSize, clearance?: number): ModelRelocation[] {
  if (models.length === 0) return [];
  const wide = widest(models);
  const pitch = wide + GAP;
  // The anchor's farthest edge from its centre: a rectangle's corner, else its long half.
  const reach = anchorBase.shape === 'rect' ? Math.sqrt(anchorBase.a * anchorBase.a + anchorBase.b * anchorBase.b) / 2 : Math.max(anchorBase.a, anchorBase.b) / 2;
  let radius = reach + Math.max(GAP, clearance ?? 0) + wide / 2;   // clear of the anchor by the Ask's separation
  const placed: ModelRelocation[] = [];
  let next = 0;
  // Ring by ring outward: a spot whose base would leave the table is skipped (pulling it back on would bring it toward the anchor).
  for (let rings = 0; next < models.length && rings < MAX_RINGS; rings++, radius += pitch) {
    const room = Math.max(1, Math.floor(2 * Math.PI * radius / pitch));
    for (let i = 0; i < room && next < models.length; i++) {
      const angle = 2 * Math.PI * i / room;
      const m = models[next]!;
      const half = Math.max(m.slot.base.a, m.slot.base.b) / 2;
      const x = anchor.position.x + radius * Math.cos(angle);
      const y = anchor.position.y + radius * Math.sin(angle);
      if (x < half || x > size.width - half || y < half || y > size.length - half) continue;
      placed.push({ modelId: m.id, from: m.location, to: { kind: 'table', placement: { position: { x, y, z: anchor.position.z }, yaw: { degrees: 0 } } } });
      next++;
    }
  }
  return placed;
}

/**
 * A pick's eligible ids, less, at a deploy question, a unit with no model on the shelf: one put inside a Transport since the Ask opened
 * deploys with the Transport (passengers spec), and naming it is the server's deploy_inside breach.
 */
export function pickable(p: Projection, ask: Ask): string[] {
  if (ask.legal.kind !== 'pieces') return [];
  const eligible = ask.legal.eligible;
  if (!DEPLOY_QUESTIONS.includes(ask.question)) return [...eligible];
  return eligible.filter((id) => !p.units.has(id) || [...p.models.values()].some((m) => m.unitId === id && m.location.kind === 'shelf'));
}

/** The eligible ids the table draws no node for: a unit none of whose models is on the table, or a model that is not. A shelved unit can only be picked from the panel. */
export function offTable(p: Projection, eligible: Iterable<string>): string[] {
  return [...eligible].filter((id) => {
    if (p.units.has(id)) return ![...p.models.values()].some((m) => m.unitId === id && m.location.kind === 'table');
    const model = p.models.get(id);
    return model !== undefined && model.location.kind !== 'table';
  });
}
