-- 017_unit_reserve.sql: a unit the army holds off the table until a rule places it (Spawn, Split, Reinforcement): the rule and the model slot or unit
-- holding it (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Readings of the batches, batch 7).
alter table management.unit add column reserve_property text, add column reserve_of uuid,
    add constraint unit_reserve_pair check ((reserve_property is null) = (reserve_of is null));
