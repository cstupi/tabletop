// viewer/src/lib/api/types.ts
// Management's DTOs, typed by hand from client/src/Core/ManagementDtos.cs and the server records (they are not generated).

import type { Enforcement, GameInfo, GameStatus, SchedulerShape } from '../protocol/wire.generated';

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
