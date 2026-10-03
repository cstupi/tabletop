-- 005_spell_library.sql: army books in a pinned set, the four effect kinds and two predicates the spell library adds, and the
-- army book each imported model slot came from (docs/superpowers/specs/2026-10-02-spell-library-design.md).
create table management.book (
    properties_hash text not null references management.properties(content_hash) on delete cascade,
    uid             text not null,
    name            text not null,
    version         text,
    primary key (properties_hash, uid)
);
create table management.book_spell (
    properties_hash text not null,
    book_uid        text not null,
    entry_name      text not null,
    ordinal         int  not null,
    primary key (properties_hash, book_uid, entry_name),
    foreign key (properties_hash, book_uid)   references management.book(properties_hash, uid) on delete cascade,
    foreign key (properties_hash, entry_name) references management.catalog_entry(properties_hash, name) on delete cascade
);

alter table management.effect drop constraint effect_kind_check;
alter table management.effect add constraint effect_kind_check check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
    'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers',
    'count_as_terrain', 'take_test', 'set_condition', 'move'));
alter table management.effect add column target_terrain text check (target_terrain in ('difficult', 'dangerous'));
alter table management.effect add column target_condition text;

alter table management.effect_condition drop constraint effect_condition_kind_check;
alter table management.effect_condition add constraint effect_condition_kind_check check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result',
    'actor_state', 'position_terrain', 'round_number', 'usage_count', 'holds_property', 'most_models'));

alter table management.model_slot add column army_book text;
