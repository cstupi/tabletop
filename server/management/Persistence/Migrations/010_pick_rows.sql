-- 010_pick_rows.sql: a pick entry may require a rule of its target ("with Caster", "with Tough"), and remove wounds ("remove D3 wounds from it")
-- (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Authored rules, batch 2).
alter table management.catalog_entry add column target_requires text check (target_requires is null or target_side is not null);
alter table management.effect drop constraint effect_kind_check;
alter table management.effect add constraint effect_kind_check check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
    'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers',
    'count_as_terrain', 'take_test', 'set_condition', 'move', 'place', 'remove_wounds'));
