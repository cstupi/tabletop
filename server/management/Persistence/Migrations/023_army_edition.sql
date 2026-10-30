-- 023_army_edition.sql: an army is a 3.5 or a 3.6 Army Forge list (rule-set versions spec). Existing rows are 3.5; a list whose
-- rules carry a name only 3.6.0 books use was imported after Army Forge switched (2026-10-29T23:48Z) and is 3.6. The names
-- exclude Armor and Shred, which 3.5 books carry too. A name may sit on the unit, a model slot or a weapon; each is matched
-- on its own. Re-runnable: the column, the backfill and the check are each safe to apply twice.
alter table management.army add column if not exists edition text not null default '3.5';

with name_36(name) as (values
    ('Bravery Aura'), ('Anti-Cover'), ('Anti-Regen'), ('Unique Hero'), ('Crossing Strike'), ('Piercing Charge'),
    ('Counter in Melee'), ('Spell Resistance'), ('Versatile Expert'), ('Assault'), ('Cumbersome'), ('Spell Amplifier'),
    ('Smash'), ('Pierce'), ('Decimate'), ('Scourge'), ('Purge'), ('Imprecise')),
marked(army_id) as (
    select u.army_id from management.unit u
    join management.unit_property p on p.unit_id = u.id join name_36 r on r.name = p.name
    union
    select u.army_id from management.unit u join management.model_slot s on s.unit_id = u.id
    join management.slot_property p on p.slot_id = s.id join name_36 r on r.name = p.name
    union
    select u.army_id from management.unit u join management.model_slot s on s.unit_id = u.id join management.weapon w on w.slot_id = s.id
    join management.weapon_property p on p.weapon_id = w.id join name_36 r on r.name = p.name)
update management.army a set edition = '3.6' from marked m where m.army_id = a.id and a.edition = '3.5';

alter table management.army drop constraint if exists army_edition_check;
alter table management.army add constraint army_edition_check check (edition in ('3.5', '3.6'));
alter table management.army alter column edition drop default;
