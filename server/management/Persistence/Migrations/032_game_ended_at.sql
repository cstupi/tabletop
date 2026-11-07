-- 032_game_ended_at.sql: when a game ended, for Home's recently ended games (docs/superpowers/specs/2026-11-07-ux-redesign-1-client-shell.md
-- → Home). Written with status = 'ended' by management (end, last leave) and the game server (a strict end); null for a game not ended,
-- and for one that ended before this column.
alter table management.game
    add column if not exists ended_at timestamptz;
