-- 029_shared_folders.sql: a player's Shared folder, readable by any signed-in player who types their username (shared-folders spec).
-- The display name is not unique, so the lookup key is a username of its own: unique ignoring case, unset until the player sets it.
alter table management.account add column username text
    constraint account_username_format check (username ~ '^[A-Za-z0-9_-]{3,32}$');
create unique index account_username_idx on management.account (lower(username));

-- At most one Shared folder per account, and only at the top level; what is filed in its subtree is shared.
alter table management.folder add column shared boolean not null default false,
    add constraint folder_shared_top_level check (not shared or parent_id is null);
create unique index folder_shared_idx on management.folder (owner_id) where shared;

-- One row per copy from a Shared folder: the copier's daily cap counts them.
create table management.shared_copy (
    id         uuid primary key,
    copier_id  uuid not null references management.account(id) on delete cascade,
    created_at timestamptz not null default now()
);
create index shared_copy_copier_idx on management.shared_copy (copier_id, created_at);
