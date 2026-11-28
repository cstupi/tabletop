-- 037_participant_left_at.sql: a seat whose army is in play outlives its player's Leave. The log names the seat's units, so deleting
-- its army snapshot made the game impossible to hydrate; the row stays, marked left, and a later Join of the account clears the mark.
alter table management.participant add column left_at timestamptz;
