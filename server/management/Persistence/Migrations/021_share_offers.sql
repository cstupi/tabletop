-- 021_share_offers.sql: sharing is an offer of assets to one named player, who accepts or declines it (shared-models spec). Nothing
-- stays visible to everyone: an asset shared under 020 is offered to no one.
drop index management.asset_shared_idx;
alter table management.asset drop column shared_at;

create table management.share_offer (
    id           uuid primary key,
    sharer_id    uuid not null references management.account(id) on delete cascade,
    recipient_id uuid not null references management.account(id) on delete cascade,
    folder_name  text check (char_length(folder_name) <= 64),          -- the folder's name when a folder was offered
    state        text not null default 'open' check (state in ('open', 'accepted', 'declined', 'withdrawn')),
    created_at   timestamptz not null default now(),
    decided_at   timestamptz,
    check (sharer_id <> recipient_id)
);
create index share_offer_sharer_idx on management.share_offer (sharer_id, created_at);
create index share_offer_recipient_idx on management.share_offer (recipient_id, state);

-- The offered assets: the set is fixed when the offer is made; an asset its owner deletes leaves the offer.
create table management.share_offer_asset (
    offer_id uuid not null references management.share_offer(id) on delete cascade,
    asset_id uuid not null references management.asset(id) on delete cascade,
    primary key (offer_id, asset_id)
);
create index share_offer_asset_asset_id_idx on management.share_offer_asset (asset_id);

-- A recipient who blocked a sharer sees none of the sharer's offers.
create table management.share_block (
    recipient_id uuid not null references management.account(id) on delete cascade,
    sharer_id    uuid not null references management.account(id) on delete cascade,
    created_at   timestamptz not null default now(),
    primary key (recipient_id, sharer_id)
);
create index share_block_sharer_id_idx on management.share_block (sharer_id);
