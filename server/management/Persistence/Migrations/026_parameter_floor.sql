-- 026_parameter_floor.sql: a parameter change "to a min. of N" (3.6.0 Fortified, Fortified Growth, Guardian, Primeborn: "AP(-1) to a min. of AP(0)")
-- carries a floor like a stat change does; the 014 column check allowed modify_stat only.
alter table management.effect drop constraint if exists effect_floor_check;
alter table management.effect add constraint effect_floor_check check (floor is null or kind in ('modify_stat', 'modify_parameter'));
