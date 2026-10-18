-- 018_participant_seats.sql: an account may hold several teams in one game (self-play), one participant row per team, each with its own army
-- (game-flow spec → One account, several teams). At most one row per (game, account, team), and at most one teamless row per account.
alter table management.participant drop constraint participant_game_id_account_id_key,
    add constraint participant_seat_key unique nulls not distinct (game_id, account_id, team_id);
