-- 013_marker_durations.sql: a row a rule places as markers on a unit, one grant per marker, kept for the game or spent together
-- (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Authored rules, batch 6).
alter table management.effect drop constraint effect_duration_check;
alter table management.effect add constraint effect_duration_check check (duration in ('interaction', 'once', 'marker', 'spent_marker'));
