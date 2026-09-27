-- The physical schema from docs/schema.md. Enumerations are text with check constraints; ids are UUIDv7
-- generated in application code; soft references to asset are plain uuid columns with no foreign key,
-- so deleting an asset is never blocked and a placement degrades to its hitbox.
create schema management;
create schema game;
set search_path to management;

create table account (
    id   uuid primary key,
    name text not null
);

create table blob (
    owner_id  uuid   not null references account(id) on delete cascade,
    hash      text   not null,
    s3_key    text   not null,
    byte_size bigint not null,
    primary key (owner_id, hash)
);

create table asset (
    id                  uuid primary key,
    owner_id            uuid not null references account(id) on delete cascade,
    blob_hash           text not null,
    name                text not null,
    kind                text not null check (kind in ('miniature', 'terrain', 'texture')),
    scale               double precision not null default 1,
    orientation_pitch   double precision not null default 0,
    orientation_yaw     double precision not null default 0,
    orientation_roll    double precision not null default 0,
    modelled_base_shape text check (modelled_base_shape in ('circle', 'oval', 'rect')),
    modelled_base_a     double precision,
    modelled_base_b     double precision,
    foreign key (owner_id, blob_hash) references blob(owner_id, hash),
    check ((kind = 'miniature') = (modelled_base_shape is not null))
);

create table asset_volume (
    id          uuid primary key,
    asset_id    uuid not null references asset(id) on delete cascade,
    kind        text not null check (kind in ('solid', 'area')),
    footprint   text not null check (footprint in ('circle', 'rect')),
    dim_a       double precision not null,
    dim_b       double precision not null,
    offset_x    double precision not null default 0,
    offset_y    double precision not null default 0,
    yaw         double precision not null default 0,
    bottom      double precision not null default 0,
    top         double precision not null,
    impassable  boolean not null default false,
    blocks_los  boolean not null default false,
    see_in_out  boolean not null default false,
    cover       boolean not null default false,
    difficult   boolean not null default false,
    dangerous   boolean not null default false,
    notes       text
);

create table folder (
    id        uuid primary key,
    owner_id  uuid not null references account(id) on delete cascade,
    parent_id uuid references folder(id) on delete cascade,
    name      text not null
);

create table asset_folder (
    asset_id  uuid not null references asset(id) on delete cascade,
    folder_id uuid not null references folder(id) on delete cascade,
    primary key (asset_id, folder_id)
);

create table army (
    id          uuid primary key,
    owner_id    uuid not null references account(id) on delete cascade,
    name        text not null,
    game_system text not null,
    source      text not null
);

create table unit (
    id      uuid primary key,
    army_id uuid not null references army(id) on delete cascade,
    ordinal int  not null,
    name    text not null,
    unique (army_id, ordinal)
);
create table unit_stat     (unit_id uuid not null references unit(id) on delete cascade, stat text not null, value text not null, primary key (unit_id, stat));
create table unit_property (unit_id uuid not null references unit(id) on delete cascade, name text not null, parameter int, primary key (unit_id, name));

create table model_slot (
    id         uuid primary key,
    unit_id    uuid not null references unit(id) on delete cascade,
    ordinal    int  not null,
    name       text not null,
    base_shape text not null check (base_shape in ('circle', 'oval', 'rect')),
    base_a     double precision not null,
    base_b     double precision not null,
    height     double precision not null,
    asset_id   uuid,            -- soft reference: appearance only, no FK
    unique (unit_id, ordinal)
);
create table slot_stat     (slot_id uuid not null references model_slot(id) on delete cascade, stat text not null, value text not null, primary key (slot_id, stat));
create table slot_property (slot_id uuid not null references model_slot(id) on delete cascade, name text not null, parameter int, primary key (slot_id, name));

create table weapon (
    id      uuid primary key,
    slot_id uuid not null references model_slot(id) on delete cascade,
    name    text not null
);
create table weapon_stat     (weapon_id uuid not null references weapon(id) on delete cascade, stat text not null, value text not null, primary key (weapon_id, stat));
create table weapon_property (weapon_id uuid not null references weapon(id) on delete cascade, name text not null, parameter int, primary key (weapon_id, name));
-- Properties: the pinned data half of a ruleset, eight tables, addressed by content hash.
create table properties (
    content_hash text primary key,
    name         text not null,
    game_rules   text not null
);
create table action (properties_hash text not null references properties(content_hash) on delete cascade, name text not null, distance double precision not null, primary key (properties_hash, name));
create table roll   (properties_hash text not null references properties(content_hash) on delete cascade, name text not null, primary key (properties_hash, name));

create table stat_definition (
    properties_hash text not null references properties(content_hash) on delete cascade,
    level           text not null check (level in ('unit', 'model', 'weapon')),
    name            text not null,
    kind            text not null check (kind in ('target_number', 'distance', 'count', 'dice_expression')),
    primary key (properties_hash, level, name)
);

create table catalog_entry (
    properties_hash text not null references properties(content_hash) on delete cascade,
    name            text not null,
    kind            text not null check (kind in ('unit', 'model', 'weapon', 'spell')),
    takes_parameter boolean not null default false,
    aggregation     text check (aggregation in ('all', 'any', 'majority', 'none')),
    cost            int,
    faction         text,
    rule_text       text not null,
    target_side     text check (target_side in ('friendly', 'enemy')),
    target_grain    text check (target_grain in ('unit', 'model')),
    target_count    int,
    target_range    double precision,
    primary key (properties_hash, name),
    check ((kind = 'model') = (aggregation is not null)),
    check ((kind = 'spell') = (cost is not null and target_side is not null and target_grain is not null and target_count is not null and target_range is not null)),
    check (kind <> 'spell' or takes_parameter = false)
);

create table effect (
    id                    uuid primary key,
    properties_hash       text not null,
    entry_name            text not null,
    parent_effect_id      uuid references effect(id) on delete cascade,
    subject               text not null check (subject in ('self', 'opponent')),
    ordinal               int  not null,
    kind                  text not null check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
                                                        'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers')),
    target_roll           text,
    target_stat_level     text,   -- with target_stat: stat_definition's key is (properties_hash, level, name), so the level is part of the reference
    target_stat           text,
    target_entry          text,
    target_action         text,
    target_quantity       text check (target_quantity in ('hits', 'wounds')),
    target_result         text check (target_result in ('wound', 'morale_test')),
    target_modifier       text check (target_modifier in ('cover', 'to_hit_penalties', 'range_penalties')),
    amount                int,
    amount_from_parameter boolean not null default false,
    threshold             int,
    cap_kind              text check (cap_kind in ('models_in_target_unit')),
    replacement           text,
    duration              text not null default 'interaction' check (duration in ('interaction', 'once')),
    foreign key (properties_hash, entry_name)    references catalog_entry(properties_hash, name) on delete cascade,
    foreign key (properties_hash, target_roll)   references roll(properties_hash, name),
    foreign key (properties_hash, target_stat_level, target_stat) references stat_definition(properties_hash, level, name),
    foreign key (properties_hash, target_entry)  references catalog_entry(properties_hash, name),
    foreign key (properties_hash, target_action) references action(properties_hash, name),
    check ((target_stat is null) = (target_stat_level is null)),
    check (threshold is null or kind = 'roll'),
    check (cap_kind is null or kind = 'multiply'),
    check (replacement is null or kind = 'replace_stat')
);

create table effect_condition (
    id         uuid primary key,
    effect_id  uuid not null references effect(id) on delete cascade,
    kind       text not null check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result', 'actor_state', 'position_terrain', 'round_number', 'usage_count')),
    subject    text check (subject in ('self', 'opponent')),
    comparison text not null check (comparison in ('eq', 'neq', 'gte', 'lte', 'gt', 'lt')),
    number     int
);
create table condition_value (condition_id uuid not null references effect_condition(id) on delete cascade, value text not null, primary key (condition_id, value));

-- Games. The game server writes these rows while status is lobby or setup, and reads only from playing on.
create table game (
    id                  uuid primary key,
    name                text not null,
    status              text not null check (status in ('lobby', 'setup', 'playing', 'ended')),
    game_rules          text not null,
    properties_hash     text not null references properties(content_hash),
    scheduler_shape     text not null check (scheduler_shape in ('player-turn', 'alternating-activation')),
    enforcement         text not null check (enforcement in ('silent', 'warn', 'strict')),
    waits_between_steps boolean not null default false,
    rng_seed            bigint not null,                                    -- the stored dice seed, written once at creation; no default, so omitting it fails loudly
    created_by          uuid not null references account(id),
    created_at          timestamptz not null default now()
);

create table team (
    id      uuid primary key,
    game_id uuid not null references game(id) on delete cascade,
    ordinal int  not null,
    unique (game_id, ordinal)
);

create table participant (
    id            uuid primary key,
    game_id       uuid not null references game(id) on delete cascade,      -- team is nullable, so the game link is here
    team_id       uuid references team(id) on delete set null,
    account_id    uuid not null references account(id) on delete cascade,
    army_id       uuid references army(id) on delete set null,              -- the lobby's army pick, until the snapshot is written
    army_snapshot jsonb,
    unique (game_id, account_id)
);

create table table_state (
    id               uuid primary key,
    owner_id         uuid references account(id) on delete cascade,
    game_id          uuid references game(id) on delete cascade,
    name             text,
    width            double precision not null,
    length           double precision not null,
    texture_builtin  text,
    texture_asset_id uuid,        -- soft reference
    check ((owner_id is null) <> (game_id is null)),
    check ((texture_builtin is null) <> (texture_asset_id is null))
);

create table terrain_placement (
    id             uuid primary key,
    table_state_id uuid not null references table_state(id) on delete cascade,
    builtin        text,
    asset_id       uuid,          -- soft reference
    pos_x double precision not null, pos_y double precision not null, pos_z double precision not null default 0,
    yaw   double precision not null default 0,
    scale double precision not null default 1,
    check ((builtin is null) <> (asset_id is null))
);

create table terrain_volume (
    id                   uuid primary key,
    terrain_placement_id uuid not null references terrain_placement(id) on delete cascade,
    kind       text not null check (kind in ('solid', 'area')),
    footprint  text not null check (footprint in ('circle', 'rect')),
    dim_a double precision not null, dim_b double precision not null,
    offset_x double precision not null default 0, offset_y double precision not null default 0,
    yaw double precision not null default 0, bottom double precision not null default 0, top double precision not null,
    impassable boolean not null default false, blocks_los boolean not null default false, see_in_out boolean not null default false,
    cover boolean not null default false, difficult boolean not null default false, dangerous boolean not null default false,
    notes text
);

create table marker (
    id              uuid primary key,
    table_state_id  uuid not null references table_state(id) on delete cascade,
    kind            text not null check (kind in ('objective', 'token')),
    pos_x double precision not null, pos_y double precision not null, pos_z double precision not null default 0,
    yaw             double precision not null default 0,
    diameter        double precision not null,
    label           text,
    visual_builtin  text,
    visual_asset_id uuid,         -- soft reference
    check ((visual_builtin is null) <> (visual_asset_id is null))
);

create table deployment_zone (
    id             uuid primary key,
    table_state_id uuid not null references table_state(id) on delete cascade,
    shape          text not null,   -- a zone shape id from Tabletop.Geometry.ZoneStyles, e.g. 'frontline/1'
    team_ordinal   int  not null
);

-- Indexes on the foreign-key columns that reads and cascades look up by. A column that already leads a
-- primary key or unique constraint has one; the rest are here.
create index asset_owner_id_blob_hash_idx            on asset (owner_id, blob_hash);
create index asset_volume_asset_id_idx               on asset_volume (asset_id);
create index folder_owner_id_idx                     on folder (owner_id);
create index folder_parent_id_idx                    on folder (parent_id);
create index asset_folder_folder_id_idx              on asset_folder (folder_id);
create index army_owner_id_idx                       on army (owner_id);
create index weapon_slot_id_idx                      on weapon (slot_id);
create index effect_properties_hash_entry_name_idx   on effect (properties_hash, entry_name);
create index effect_parent_effect_id_idx             on effect (parent_effect_id);
create index effect_condition_effect_id_idx          on effect_condition (effect_id);
create index game_properties_hash_idx                on game (properties_hash);
create index game_created_by_idx                     on game (created_by);
create index participant_account_id_idx              on participant (account_id);
create index participant_team_id_idx                 on participant (team_id);
create index participant_army_id_idx                 on participant (army_id);
create index table_state_owner_id_idx                on table_state (owner_id);
create index table_state_game_id_idx                 on table_state (game_id);
create index terrain_placement_table_state_id_idx    on terrain_placement (table_state_id);
create index terrain_volume_terrain_placement_id_idx on terrain_volume (terrain_placement_id);
create index marker_table_state_id_idx               on marker (table_state_id);
create index deployment_zone_table_state_id_idx      on deployment_zone (table_state_id);

-- The game server's schema: one table, append-only. No FK across the boundary; account deletion is application logic.
create table game.game_event (
    game_id uuid   not null,
    seq     bigint not null,
    kind    text   not null,
    payload jsonb  not null,
    primary key (game_id, seq)
);
