-- 004_properties_seeded_at.sql: when the game server last seeded each set. GET /v1/properties lists the latest per (game_rules, name).
alter table management.properties
    add column seeded_at timestamptz not null default now();
