-- 009_target_line_of_sight.sql: a pick entry may target out of sight; null is in sight (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → the pick).
alter table management.catalog_entry add column target_line_of_sight boolean check (target_line_of_sight is null or target_side is not null);
