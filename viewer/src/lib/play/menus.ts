// viewer/src/lib/play/menus.ts
// Where GameScreen.Play opens each menu (ItemsFor, OpenTeamMenu, Roster.UnitMenuRequested, the table's Alt+right-click), and what an item's
// input needs before it sends. Godot offers the model menu to any participant (the server judges hand edits), the roster menu only for
// my team, and nothing to a watcher.

import type { Projection } from '../live/apply';
import type { Command, GameEvent, GameInfo, Point3 } from '../protocol/wire.generated';
import type { AskView } from './askView';
import type { MarkerLocks } from './markerLocks';
import {
  forBoarding, forFrame, forMarker, forModel, forRosterUnit, forTeam, forUnit, knownActionNames, offTable, type MenuItem,
} from './menuModel';
import type { Names } from './names';
import type { GrantChoice } from './ruleTexts';

export interface MenuContext {
  readonly game: GameInfo; readonly p: Projection; readonly log: readonly GameEvent[];
  readonly watching: boolean; readonly myTeam: string | null; readonly mine: AskView | null; readonly grantable: readonly GrantChoice[];
  readonly deployInside: ReadonlyMap<string, readonly string[]>; readonly maySetAside: ReadonlySet<string>; readonly locks: MarkerLocks;
}

/** ItemsFor: a model (boarding, then the model's items and the frame's; Shift the unit's), else a marker; [] for a watcher. */
export function pieceMenu(ctx: MenuContext, id: string, shift: boolean): MenuItem[] {
  if (ctx.watching) return [];
  const { p } = ctx;
  const model = p.models.get(id);
  if (model !== undefined) {
    const known = knownActionNames(p, ctx.log);
    const boarding = forBoarding(p, ctx.mine?.ask ?? null, id);
    return [...boarding, ...(shift ? forUnit(p, model.unitId, known, ctx.grantable) : [...forModel(p, id, known), ...forFrame(p)])];
  }
  return forMarker(ctx.game, p, id, ctx.locks);
}

/** OpenTeamMenu: the team's items, then each of its models off the table. */
export function teamMenu(ctx: MenuContext, teamId: string, n: Names): MenuItem[] {
  return ctx.watching ? [] : [...forTeam(ctx.game, ctx.p, teamId), ...offTable(ctx.p, teamId, n)];
}

/** Roster.UnitMenuRequested: my team's units only. */
export function rosterMenu(ctx: MenuContext, unitId: string): MenuItem[] {
  return ctx.watching ? [] : forRosterUnit(ctx.game, ctx.p, unitId, ctx.myTeam, ctx.deployInside, ctx.maySetAside);
}


/** What choosing an item asks for next: send now, a dialog (integer, text, choice), or the next piece or table click. */
export type MenuStep =
  | { kind: 'send'; command: Command }
  | { kind: 'local' }
  | { kind: 'dialog'; input: 'integer' | 'text' | 'choice'; choices: readonly string[] }
  | { kind: 'pick'; what: 'piece' | 'table' }
  | { kind: 'invalid' };

/** `input` is null when the player has not been asked yet; an input that does not parse is invalid. */
export function choose(item: MenuItem, input: string | null): MenuStep {
  if (item.local !== undefined) return { kind: 'local' };
  if (input === null) {
    switch (item.input) {
      case 'integer': case 'text': case 'choice': return { kind: 'dialog', input: item.input, choices: item.choices };
      case 'pieceId': return { kind: 'pick', what: 'piece' };
      case 'tablePoint': return { kind: 'pick', what: 'table' };
      case 'none': break;
    }
  }
  const command = item.build(input);
  return command === null ? { kind: 'invalid' } : { kind: 'send', command };
}
