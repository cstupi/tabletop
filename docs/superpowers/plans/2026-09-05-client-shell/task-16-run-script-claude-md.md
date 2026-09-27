# Task 16: The run script, `CLAUDE.md`, and the whole-tree check

**Files:**
- Create: `scripts/client`
- Modify: `CLAUDE.md` (opening paragraph, Commands, Layout, Conventions)

**Interfaces:**
- Produces: `./scripts/client` — builds the three libs and the client from WSL, then launches `./scripts/godot`; `CLAUDE.md` names the client's structure, the Core rule, the test project, the check script and the run order.

- [ ] **Step 1: Write `scripts/client`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# Run the client: build in WSL first so the referenced libs are current, then launch the Windows Godot binary on client/.
# Godot's import wipes client/.godot/mono/temp/bin/, so let the editor rebuild if scripts fail to load after an import.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build client/Tabletop.csproj
./scripts/check-client
exec ./scripts/godot "$@"
```

- [ ] **Step 2: Edit `CLAUDE.md`**

**Ownership rule for `CLAUDE.md`**, for parallel execution: a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

In **Commands**, append at the end of the block:

```bash
./scripts/client             # build, run scripts/check-client, launch the Godot client on Windows
./scripts/check-client       # no physics body in a scene, no rules type or Godot in client/src/Core
dotnet test tests/Tabletop.Client.Tests   # the client's Godot-free core
```

In **Layout**, replace the `client/` bullet's children (the indented lines under "- `client/` —", currently the scaffold's `Tabletop.csproj`, `src/`, `scenes/` lines) with:

```
  - `src/Core/` — Godot-free: `Session`, `ManagementClient`, `ClientGameSocket`, `AssetCache`, `BuiltinContent`,
    `TerrainGenerators`, the view models. Compiled into `tests/Tabletop.Client.Tests` from source, so a `using Godot`
    here breaks the tests. May reference only the three libs and the BCL.
  - `src/Screens/`, `src/Table/`, `src/Panels/` — the Godot layer; thin over `Core` and `Tabletop.Geometry`.
  - `scenes/` — `Main.tscn` is the root that swaps `Login`, `Home`, `Game`, `UpdateRequired`; `TableView.tscn` is the table.
    Scenes are written minimal by hand and owned by the editor afterwards.
  - `content/builtin/` — the five table textures and the ten grouped terrain meshes; a missing file draws the piece's volumes.
```

Leave the `client/` parent line itself as it is. The `tests/` bullet and the *Tests:* convention already cover the client's `src/Core/` in the form the contracts plan wrote; edit neither.

In **Conventions**, append: "- The table frame's origin is the table's corner (`0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up) on the wire and in `Tabletop.Geometry` alike; nothing converts. Godot: x = X, y = Z, z = −Y, `rotation.y` = yaw (`WorldFrame`). No Godot physics; picking is ray-versus-AABB."

- [ ] **Step 3: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d && dotnet build && dotnet test && ./scripts/check-client
find client libs tests -path '*/obj/*' -maxdepth 3 -type d | grep -v -E 'Unix|Windows_NT'
git status --short
```

Then, with `./scripts/management` and `dotnet run --project server/game` running in WSL, `./scripts/client` on two accounts: log in (remember me), create a game, the second joins, teams and armies picked, setup with a generator batch, a saved layout reloaded, end setup; both see the board, presence, a hand move with its ruler, a menu edit in the log, an undo-to-here, Home and back (reconnect rebuilds, and the returning player is back on their own team with the same Ask controls), Leave. Expected: every test project green (Geometry, Content, Protocol, Management, GameServer, Client — 44 in Client), the check script silent, no `obj/` outside `Unix/` and `Windows_NT/`, and `git status` clean apart from the intended files (never `.env`, never `client/.godot/`).

- [ ] **Step 4: Commit**

```bash
git add scripts/client CLAUDE.md
git commit -m "Record the client shell in CLAUDE.md and add its run script"
```
