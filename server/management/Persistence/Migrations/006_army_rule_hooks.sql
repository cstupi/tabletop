-- 006_army_rule_hooks.sql: the place kind and its dice, add_hit adding attacks, and the at_point predicate
-- (docs/superpowers/specs/2026-10-02-army-rule-hooks-design.md).
alter table management.effect drop constraint effect_kind_check;
alter table management.effect add constraint effect_kind_check check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
    'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers',
    'count_as_terrain', 'take_test', 'set_condition', 'move', 'place'));
alter table management.effect drop constraint effect_target_quantity_check;
alter table management.effect add constraint effect_target_quantity_check check (target_quantity in ('hits', 'wounds', 'attacks'));
alter table management.effect add column amount_dice text check (amount_dice in ('d3'));

alter table management.effect_condition drop constraint effect_condition_kind_check;
alter table management.effect_condition add constraint effect_condition_kind_check check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result',
    'actor_state', 'position_terrain', 'round_number', 'usage_count', 'holds_property', 'most_models', 'at_point'));
