import type { Base, Placement, PlacedModel, TerrainPlacement } from './types';

/** DragBodies.Walls: inside a move Ask the ids it lists as impassable, else every volume declared Impassable. */
export function wallsOf(terrain: readonly TerrainPlacement[], impassableVolumeIds?: readonly string[]): string[] {
  const volumes = terrain.flatMap((t) => t.volumes);
  if (impassableVolumeIds) return volumes.filter((v) => impassableVolumeIds.includes(v.id)).map((v) => v.id);
  return volumes.filter((v) => declares(v.volume.declarations, 'impassable')).map((v) => v.id);
}

/** The wire writes the flags as names joined by ", " ("impassable, blocks_line_of_sight"). */
function declares(declarations: unknown, name: string): boolean {
  return typeof declarations === 'string' && declarations.split(',').some((d) => d.trim() === name);
}

/** A placed model from a wire slot's base and height. */
export function placed(slot: { readonly base: Base; readonly height: number }, placement: Placement): PlacedModel {
  return { body: { base: slot.base, height: slot.height }, placement };
}
