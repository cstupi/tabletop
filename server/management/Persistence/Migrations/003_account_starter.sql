-- 003_account_starter.sql: when the account's library was given the starter miniatures; null until then (spec 2026-10-01-starter-meshes-design).
alter table management.account
    add column starter_seeded_at timestamptz;
