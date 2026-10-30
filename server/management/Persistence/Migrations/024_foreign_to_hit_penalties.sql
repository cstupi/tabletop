-- 024_foreign_to_hit_penalties.sql: 3.6.0 p.15 Unstoppable ignores the negative hit modifiers not from the holder's own rules
-- (TargetModifier.ForeignToHitPenalties).
alter table management.effect drop constraint if exists effect_target_modifier_check;
alter table management.effect add constraint effect_target_modifier_check check (target_modifier in ('cover', 'to_hit_penalties', 'range_penalties', 'foreign_to_hit_penalties'));
