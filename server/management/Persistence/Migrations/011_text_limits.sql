-- 011_text_limits.sql: the length limits the API enforces (Http/Limits.cs), as a backstop for every writer. NOT VALID: rows written
-- before the limits keep their values; every insert and update from here on is checked.
alter table management.account     add constraint account_name_length     check (char_length(name) <= 64) not valid;
alter table management.folder      add constraint folder_name_length      check (char_length(name) <= 64) not valid;
alter table management.asset       add constraint asset_name_length       check (char_length(name) <= 64) not valid;
alter table management.army        add constraint army_name_length        check (char_length(name) <= 64) not valid;
alter table management.army        add constraint army_source_length      check (char_length(source) <= 512) not valid;
alter table management.game        add constraint game_name_length        check (char_length(name) <= 64) not valid;
alter table management.table_state add constraint table_state_name_length check (name is null or char_length(name) <= 64) not valid;
