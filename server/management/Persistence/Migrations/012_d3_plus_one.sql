-- 012_d3_plus_one.sql: a placement "anywhere fully within D3+1"" (Bounding, Dash) rolls a D3 and adds one
-- (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Authored rules, batch 4).
alter table management.effect drop constraint effect_amount_dice_check;
alter table management.effect add constraint effect_amount_dice_check check (amount_dice in ('d3', 'd3+1'));
