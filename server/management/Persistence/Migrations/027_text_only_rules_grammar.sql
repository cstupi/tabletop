-- 027_text_only_rules_grammar.sql: the grammar the 3.6.0 text-only rules play by (docs/superpowers/specs/2026-10-30-gf-3-6-0-text-only-rules.md
-- → The additions): kinds count_wounds (Scourge) and remove_spell_tokens (Spell Drain); predicates weapon_range (Pierce, Smash) and
-- remaining_size (Destructive, Decimate); suppress_modifiers positive_defense (Pierce); duration one_marker (Piercing Tag, Precision Tag);
-- add_hit optional (Predator's "may"); a pick's once-per-round targets (Mind Control) and its count from the parameter (the Tags).
alter table management.effect drop constraint if exists effect_kind_check;
alter table management.effect add constraint effect_kind_check check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
    'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers',
    'count_as_terrain', 'take_test', 'set_condition', 'move', 'place', 'remove_wounds', 'count_wounds', 'remove_spell_tokens'));

alter table management.effect_condition drop constraint if exists effect_condition_kind_check;
alter table management.effect_condition add constraint effect_condition_kind_check check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result',
    'actor_state', 'position_terrain', 'round_number', 'usage_count', 'holds_property', 'most_models', 'at_point', 'weapon_range', 'remaining_size'));

alter table management.effect drop constraint if exists effect_target_modifier_check;
alter table management.effect add constraint effect_target_modifier_check check (target_modifier in ('cover', 'to_hit_penalties', 'range_penalties', 'foreign_to_hit_penalties', 'positive_defense'));

alter table management.effect drop constraint if exists effect_duration_check;
alter table management.effect add constraint effect_duration_check check (duration in ('interaction', 'once', 'marker', 'spent_marker', 'one_marker'));

alter table management.effect add column optional boolean not null default false;
alter table management.effect add constraint effect_optional_check check (optional = false or kind = 'add_hit');

alter table management.catalog_entry add column targeting_once_per_round boolean not null default false;
alter table management.catalog_entry add column targeting_count_from_parameter boolean not null default false;
alter table management.catalog_entry add constraint catalog_entry_targeting_flags_check
    check ((targeting_once_per_round = false and targeting_count_from_parameter = false) or target_side is not null);
