-- 010_optional_rules.sql: the optional rules a game's players opted into, keys and values the ruleset defines; '{}' is the core game (docs/superpowers/specs/2026-10-15-optional-rules-design.md).
alter table management.game add column optional_rules jsonb not null default '{}'::jsonb;
