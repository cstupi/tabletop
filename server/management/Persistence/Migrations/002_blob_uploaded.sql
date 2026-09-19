-- 002_blob_uploaded.sql: the upload state between POST /v1/assets and /confirm, and what the bytes are.
alter table management.blob
    add column uploaded     boolean not null default false,
    add column content_type text    not null default 'model/gltf-binary'
        check (content_type in ('model/gltf-binary', 'image/png', 'image/jpeg', 'image/webp'));
