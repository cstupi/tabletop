-- 034_unit_points.sql: what the army list says a unit cost (its entries and their selected upgrades), written at import and shown on the unit
-- card; null for an army imported before this, until it is imported again. Nothing in the rules reads it.
alter table management.unit add column points int check (points is null or points between 0 and 100000);
