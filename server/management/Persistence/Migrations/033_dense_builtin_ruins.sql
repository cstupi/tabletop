-- 033_dense_builtin_ruins.sql: the built-in ruins and deadly vegetation are seen into and out of, not through (owner ruling; 3.6.0
-- p.12 Dense, 3.5.1 p.12 "see into and out of ... but not through"): BlocksLineOfSight with SeeInAndOut. A saved layout's or an
-- unfinished game's volume on one of these built-in meshes whose declarations are exactly that mesh's previous default gains both;
-- a volume edited away from the default, an ended game's and the 4" `ruin` (on the `ruins` mesh, already Dense) are left as they are.
update management.terrain_volume v
set blocks_los = true, see_in_out = true
from management.terrain_placement p
join management.table_state ts on ts.id = p.table_state_id
left join management.game g on g.id = ts.game_id
where p.id = v.terrain_placement_id
  and (ts.game_id is null or g.status in ('lobby', 'setup', 'playing'))
  and not v.impassable and not v.blocks_los and not v.see_in_out and not v.steep
  and (
       (p.builtin in ('ruined-corner', 'ruins') and v.cover and not v.difficult and not v.dangerous and v.dangerous_rush_charge)
    or (p.builtin = 'overgrown-ruins' and v.cover and v.difficult and not v.dangerous and not v.dangerous_rush_charge)
    or (p.builtin = 'deadly-vegetation' and v.cover and v.difficult and v.dangerous and not v.dangerous_rush_charge)
  );
