-- 014_stat_floor.sql: a stat change "to a min. of N" (Ranged Shrouding, Melee Shrouding, Darkborn) holds the stat at N or at what it was, if lower
-- (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Authored rules, batch 8).
alter table management.effect add column floor integer check (floor is null or kind = 'modify_stat');
