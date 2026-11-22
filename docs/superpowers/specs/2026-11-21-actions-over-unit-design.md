# Actions over the selected unit

Owner, 2026-11-21: *"What if we put unit actions above the unit if it is selected on your activation - maybe just the symbols leaving the
existing lower right corner options."* Both the Godot client and the browser app (owner: "both"). Nothing new on the wire.

## Behaviour

- **When:** the open Ask is mine (my seat answers it) and asks for the action of the selected unit (the activation Ask: Hold, Advance,
  Rush, Charge and any other option the server offers for that unit). Hidden for every other Ask, for another team's Ask, while watching or
  replaying, when nothing or another unit is selected, and once the answer is sent.
- **What:** one round icon button per offered option for that unit, in the option order the decision box uses, with the option's icon
  (the decision box tile's icon). An option not offered is not drawn. Tooltip: the option's word and its key cap (`Advance  2`).
- **Where:** centred above the unit, above its tallest model's top, following the camera every frame; kept inside the table's view and off
  the Hud slots (clamped, never drawn under a panel).
- **Click:** sends exactly what the decision box tile for that option sends (the same Core call). The decision box stays as it is in the
  lower right; number keys work as today. Hover lights the matching tile, and the matching tile's hover lights the button.
- **Spacing:** `Space.XS` between buttons, `Space.S` above the models (spec 2026-11-21-spacing-rules-design.md).

## Where it lives

- Client Core (Godot-free, unit-tested): `UnitActionsView.Of(ask, mySeat, selected, projection)` → the unit, the options (id, icon, word,
  key) or null. The decision box and this view read the same option list.
- Client node: `client/src/Table/UnitActionsNode.cs` (or beside `UnitMarkerNodes`), projected with the camera as the unit markers are.
- Browser: a port of `UnitActionsView` in `viewer/src/lib/play/` with its agreement test against a fixture written by the client tests
  (the repo's existing fixture pattern), and a Svelte overlay over the 2D table above the selected unit's icon.

## Every step of the activation

Owner, 2026-11-21: *"where is my icons to shoot?"* and *"I have the icons above the unit to do the initial move then have to move all
over the keyboard to progress past that or I have to click in the lower right every time which is just annoying."* The bar over the
unit carries the whole activation, not only the action pick:

- **Which Asks:** every open Ask of mine whose acting unit is on the table (the activation pick, the move, Stop here or Continue, the
  shoot Ask, casting, a charge's move, strike back, a confirm); hidden while watching, replaying or for another team's Ask.
- **What:** the Ask's option tiles as round icons (as the action pick has), then the decision box's foot actions as round icons with
  their icon and key cap tooltip: the primary (Confirm the move, Fire, Cast, Continue) lit, the others plain (Stop here, Clear, Pass,
  Cancel). Exactly the decision box's actions, enabled and disabled as there (a disabled one is drawn dim with its reason as tooltip).
- **Shooting:** while I pick targets (Assign), each enemy unit I may target gets a crosshair icon above it; clicking it assigns all
  unassigned weapons to it, as clicking the unit does; the bar over my unit holds Fire and Clear.
- **Where:** above the acting unit (the activation's unit, else the selected unit of mine the Ask names), as today; it follows the camera.
- **Keys:** unchanged; the bar is a mouse path beside the keys and the decision box.
