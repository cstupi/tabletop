-- 007_properties_positions.sql: each ordered child row of a property set records its index in the document, so the set read back
-- evaluates as the document does. Null on rows seeded before this script; the game server's seeder fills a present set's positions
-- from its document, keeping row ids (a game's grants name effect ids).
alter table management.action           add column position int;
alter table management.effect           add column position int;
alter table management.effect_condition add column position int;
alter table management.condition_value  add column position int;
