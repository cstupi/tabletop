-- 020_shared_assets.sql: an asset its owner shares with every signed-in account (shared-models spec). A copy's blob row names
-- the sharer's S3 key, so an object goes only when no blob row names it; the index serves that check.
alter table management.asset add column shared_at timestamptz;
create index blob_s3_key_idx on management.blob (s3_key);
create index asset_shared_idx on management.asset (id) where shared_at is not null;
