-- 028_drop_unnamed_floor_check.sql: 014 declared the floor check inline and unnamed (Postgres named it effect_check4,
-- floor is null or kind = 'modify_stat'), so 026's drop of effect_floor_check dropped nothing and the 014 check still refused
-- a modify_parameter row with a floor (3.6.0 Fortified, Fortified Growth, Guardian, Primeborn). The generated name may differ
-- between databases, so the stale checks are found through pg_constraint.
do $$
declare c record;
begin
    for c in
        select conname from pg_constraint
        where conrelid = 'management.effect'::regclass and contype = 'c'
          and conname <> 'effect_floor_check' and pg_get_constraintdef(oid) ilike '%floor%'
    loop
        execute format('alter table management.effect drop constraint %I', c.conname);
    end loop;
end $$;
