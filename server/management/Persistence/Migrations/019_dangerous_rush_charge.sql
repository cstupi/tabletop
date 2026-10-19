-- 019_dangerous_rush_charge.sql: the seventh terrain declaration, rulebook p.12's "Dangerous when using rush/charge" (Rivers, Ruins),
-- on both volume tables (TerrainDeclarations.DangerousOnRushCharge).
alter table management.asset_volume add column dangerous_rush_charge boolean not null default false;
alter table management.terrain_volume add column dangerous_rush_charge boolean not null default false;
