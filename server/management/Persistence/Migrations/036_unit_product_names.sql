-- 036_unit_product_names.sql: the names of the official products the army list names for a unit (Army Forge's store-link labels, never a link,
-- image or description), written at import and read by the web mesh picker to rank its suggestions; null for an army imported before this.
alter table management.unit add column product_names text[] check (product_names is null or cardinality(product_names) between 1 and 16);
