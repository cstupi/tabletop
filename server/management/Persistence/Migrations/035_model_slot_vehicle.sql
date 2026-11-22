-- 035_model_slot_vehicle.sql: a vehicle, whose move path both clients draw as treads: the army list published no base for the slot's entry, so the
-- import stood it on the vehicle hull (ArmyForgeImport.DefaultBody, 221 x 119 x 113 mm). Slots imported before this still on that hull are marked.
alter table management.model_slot add column vehicle boolean not null default false;
update management.model_slot set vehicle = true
 where base_shape = 'rect' and abs(base_a - 221 / 25.4) < 1e-6 and abs(base_b - 119 / 25.4) < 1e-6 and abs(height - 113 / 25.4) < 1e-6;
