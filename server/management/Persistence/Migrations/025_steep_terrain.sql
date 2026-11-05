-- 025_steep_terrain.sql: the eighth terrain declaration, Steep (3.6.0 p.12 "Units moving onto steep terrain count it as being in Difficult
-- terrain, but units moving off it count it as open terrain"; 3.5.1 p.12 Hills "Difficult when moving up"), on both volume tables
-- (TerrainDeclarations.Steep).
alter table management.asset_volume add column if not exists steep boolean not null default false;
alter table management.terrain_volume add column if not exists steep boolean not null default false;
