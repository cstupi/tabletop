-- 010_blob_created_at.sql: when a blob row was created, so the upload sweep can delete rows whose bytes never arrived.
alter table management.blob add column created_at timestamptz not null default now();
