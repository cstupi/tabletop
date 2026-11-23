// viewer/src/lib/play/cardActions.ts
// Godot's CardActions (client/src/Core/CardActions.cs): the decision card's foot actions and their number keys. The owner, 2026-11-21:
// "if 1,2,3 decides then users should hit 1 not enter". Every action on the card is numbered in one sequence: the option tiles first, then
// the foot's actions, the primary first; past 9 none has a key. Enter and Esc stay silent aliases for the primary and Clear or Cancel.
import type { Projection } from '../live/apply';
import type { Ask } from '../protocol/wire.generated';
import { confirmLabel, type AskMode } from './askView';
import { keyCap } from './decisionCard';
import { stopLabel } from './decisionView';
import { isDisembark } from './moveRuler';

/** One action at the card's foot: its id (the box's button id), word, icon, whether it is the primary, its key (0: none), and its state. */
export interface CardAction {
  readonly id: string; readonly word: string; readonly icon: string | null; readonly primary: boolean;
  readonly key: number; readonly enabled: boolean; readonly reason: string | null;
}

const KEYED = 10;
const act = (id: string, word: string, icon: string | null, primary = false): CardAction => ({ id, word, icon, primary, key: 0, enabled: true, reason: null });

/** The foot actions of an Ask's card, the primary first and Pass last, without keys or state. */
export function actionsOf(ask: Ask, mode: AskMode, p: Projection): CardAction[] {
  const list: CardAction[] = [];
  const legal = ask.legal;
  switch (mode) {
    case 'confirm': list.push(act('confirm', confirmLabel(ask), 'ready', true)); break;
    case 'tokenSpend': list.push(act('spend', 'Spend', 'cast', true)); break;
    case 'move':
      if (legal.kind !== 'move_region') break;
      list.push(act('commit', 'Commit', 'ready', true));
      if (!isDisembark(legal)) { const word = stopLabel(p, legal); if (word !== null) list.push(act('stop', word, word.startsWith('Cast') ? 'cast' : 'hold')); }
      break;
    case 'assign': list.push(act('fire', 'Fire', 'shoot', true), act('clear', 'Clear', 'remove')); break;
    case 'allocate': list.push(act('confirm', 'Confirm', 'ready', true), act('auto', 'Auto', 'refresh'), act('clear', 'Clear', 'remove')); break;
    case 'pickPieces': if (legal.kind === 'pieces' && legal.max > 1) list.push(act('done', 'Done', 'ready', true)); break;
    default: break;
  }
  if (ask.allowPass) list.push(act('pass', ask.question === 'act_or_end_phase' ? 'End phase' : 'Pass', 'skip'));
  return list;
}

/** A placement's one action (a deploy pick placed on the table). */
export const PLACING: readonly CardAction[] = [act('place', 'Place', 'ready', true)];

/** The actions keyed after the card's numbered tiles: tile keys run 1 to `tiles`, the actions on from there. */
export function numberActions(actions: readonly CardAction[], tiles: number): CardAction[] {
  return actions.map((a, i) => ({ ...a, key: tiles + i + 1 <= KEYED ? tiles + i + 1 : 0 }));
}

/** A button's tooltip: the word and its key cap ("Fire  5"), and the reason it is dim. */
export function actionTip(a: CardAction): string {
  return (a.key > 0 ? `${a.word}  ${keyCap(a.key)}` : a.word) + (!a.enabled && a.reason !== null && a.reason !== '' ? ` · ${a.reason}` : '');
}
