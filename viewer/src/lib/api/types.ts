// viewer/src/lib/api/types.ts
// Management's DTOs, typed by hand from client/src/Core/ManagementDtos.cs and the server records (they are not generated).

import type { DeploymentZone, Enforcement, GameInfo, GameStatus, SchedulerShape, TableSize, TerrainPlacement } from '../protocol/wire.generated';

export type { Enforcement, GameInfo, GameStatus, SchedulerShape };

export interface CreateGameRequest {
  readonly name: string;
  readonly propertiesHash: string;
  readonly schedulerShape: SchedulerShape;
  readonly enforcement: Enforcement;
  readonly waitsBetweenSteps: false;
}
export interface PropertiesSummary { readonly contentHash: string; readonly name: string; readonly gameRules: string }
export interface ArmySummary {
  readonly id: string; readonly name: string; readonly gameSystem: string; readonly source: string;
  readonly units: number; readonly models: number;
}
export interface Page<T> { readonly items: readonly T[]; readonly next?: string | null }

export interface LayoutSummary { readonly id: string; readonly name: string; readonly size: TableSize; readonly terrain: number; readonly markers: number }
export interface AssetDto {
  readonly id: string; readonly name: string; readonly kind: 'model' | 'terrain' | 'texture'; readonly hash: string;
  readonly byteSize: number; readonly contentType: string; readonly uploaded: boolean; readonly scale: number;
}
export type Planet = 'generic' | 'hospitable' | 'wasteland' | 'death' | 'ruins';
export interface RandomTableRequest {
  readonly size: TableSize; readonly planet: Planet; readonly zones?: readonly DeploymentZone[]; readonly seed?: number;
}
export interface RandomTable {
  readonly seed: number; readonly terrain: readonly TerrainPlacement[]; readonly unplaced: readonly string[]; readonly unlisted: readonly string[];
}

/** GET /v1/properties/{hash}/rules: a pinned set's rule catalog row (ManagementDtos.RuleTextDto); `played` absent reads true. */
export interface RuleTextDto {
  readonly name: string; readonly kind: string; readonly takesParameter: boolean; readonly ruleText: string; readonly played?: boolean;
}
/** GET /v1/properties/{hash}/grants (ManagementDtos.GrantableEffectDto). */
export interface GrantableEffectDto { readonly entryName: string; readonly effectId: string; readonly duration: string }
