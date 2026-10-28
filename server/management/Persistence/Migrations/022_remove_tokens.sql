-- 022_remove_tokens.sql: a marker is an objective (owner ruling: "Remove tokens."). Token rows in drafts and layouts go, and the
-- kind check admits only 'objective'. In-play markers go with them: every marker_placed event, and each marker_moved or
-- marker_removed of a marker one placed in the same game. Seq gaps are harmless: nothing reads the log as contiguous, and an
-- undo names a seq, not a position.
delete from management.marker where kind = 'token';
alter table management.marker drop constraint marker_kind_check;
alter table management.marker add constraint marker_kind_check check (kind in ('objective'));

delete from game.game_event e
using (select game_id, payload->'payload'->'marker'->>'id' as marker_id from game.game_event where kind = 'marker_placed') p
where e.game_id = p.game_id
  and e.kind in ('marker_moved', 'marker_removed')
  and e.payload->'payload'->>'markerId' = p.marker_id;
delete from game.game_event where kind in ('marker_placed', 'marker_removed');
