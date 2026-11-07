-- 031_game_progress.sql: where a game in play stands, for the game list (docs/superpowers/specs/2026-11-07-ux-redesign-1-client-shell.md
-- → Home): the round, the acting team's ordinal, the accounts an open Ask waits on, and the winners' ordinals once ended. The game
-- server writes them after each run of the log; null round: never written (lobby, setup, or not hydrated since).
alter table management.game
    add column if not exists round       int,
    add column if not exists acting_team int,
    add column if not exists waiting_on  uuid[] not null default '{}',
    add column if not exists outcome     int[];
