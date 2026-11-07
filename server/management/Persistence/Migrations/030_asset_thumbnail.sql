-- 030_asset_thumbnail.sql: a small picture of an asset for the web app's tiles, rendered by the uploader's browser (UX redesign, web).
-- Null: none. A copy names its source's object, as its blob row names the source's file; an object goes when no row names it.
alter table management.asset add column thumbnail_key text;
create index asset_thumbnail_key_idx on management.asset (thumbnail_key) where thumbnail_key is not null;
