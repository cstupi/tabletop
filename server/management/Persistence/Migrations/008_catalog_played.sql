-- 008_catalog_played.sql: a generated text-only entry is not played (docs/superpowers/specs/2026-10-09-army-book-rules-design.md → Played flag).
alter table management.catalog_entry add column played boolean not null default true;
alter table management.catalog_entry add constraint catalog_entry_played_check check (played or kind <> 'spell');
