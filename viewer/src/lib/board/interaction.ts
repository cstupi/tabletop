// viewer/src/lib/board/interaction.ts
// What the board offers whoever drives it: setup (C2) and play (D) hand it a BoardInteraction and overlays; the board turns
// pointer and key events into these calls. Table inches throughout, never pixels.

import type { Point } from './transform';

export type BoardHit =
  | { readonly kind: 'model'; readonly id: string }
  | { readonly kind: 'terrain'; readonly id: string; readonly volumeId: string }
  | { readonly kind: 'marker'; readonly id: string };

export interface Modifiers { readonly shift: boolean; readonly ctrl: boolean; readonly alt: boolean }

export const NO_MODIFIERS: Modifiers = { shift: false, ctrl: false, alt: false };

/** Every handler is optional; without onDragStart a press pans. */
export interface BoardInteraction {
  /** Hit testing; default pickAt(scene, at, the board's model style): models, then markers, then terrain, last drawn first. */
  pick?(at: Point): BoardHit | null;
  onHover?(hit: BoardHit | null, at: Point): void;
  /** A press and release under the drag threshold (3 px); `at` is where the pointer came up, in table inches. */
  onSelect?(hit: BoardHit | null, mods: Modifiers, at: Point): void;
  /** Left press on a hit, moved past the threshold: true takes the drag, false pans. */
  onDragStart?(hit: BoardHit, at: Point, mods: Modifiers): boolean;
  onDragMove?(at: Point, mods: Modifiers): void;
  onDragEnd?(at: Point, mods: Modifiers): void;
  /** Escape, pointer cancel or the canvas losing the pointer mid-drag. */
  onDragCancel?(): void;
  /** Right click; `screen` is canvas pixels, for a menu; `mods` are the keys held when it went down. */
  onContextMenu?(hit: BoardHit | null, at: Point, screen: Point, mods?: Modifiers): void;
  /** Keys while the canvas has focus (it is focusable); true when handled. Escape cancels a drag before this is asked. */
  onKey?(key: string, mods: Modifiers): boolean;
}

/** Drawn after the scene, in table inches: ghosts, landing rings, rulers, rings. */
export type Overlay = (ctx: CanvasRenderingContext2D, project: (p: Point) => Point, pixelsPerInch: number) => void;
