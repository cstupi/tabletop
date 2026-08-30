# Data model

Every entity the eight design sessions produced, and how they relate.

**Conceptual, not a schema.** No tables, no columns, no indices. Persistence layout was
deferred deliberately in three separate specs, and this document keeps that line: it says what
exists and what points at what.

The geometric fields carry real types, defined in
[geometry values](superpowers/specs/2026-08-29-geometry-values-design.md). The rest are still
placeholders — `stats`, `list`, `ref`, `profile` — awaiting the identity and content sessions.
`units` means world units, which are inches.

Where this and a design doc disagree, the design doc is authoritative — this is a view
over them, not a source. [`docs/schema.md`](schema.md) is the physical expression of the same
model: tables, columns and constraints.

```mermaid
erDiagram
    ACCOUNT ||--o{ ARMY : owns
    ACCOUNT ||--o{ ASSET : owns
    ACCOUNT ||--o{ BLOB : owns
    ACCOUNT ||--o{ FOLDER : owns
    ACCOUNT ||--o{ TABLESTATE : "saves as a layout"
    ACCOUNT ||--o{ PARTICIPANT : "plays as"

    ARMY ||--|{ UNIT : contains
    UNIT ||--|{ MODELSLOT : contains
    MODELSLOT }o..o| ASSET : "appearance, soft"
    MODELSLOT ||--o{ WEAPON : loadout

    ASSET }o--|| BLOB : "bytes"
    FOLDER ||--o{ FOLDER : nests
    FOLDER }o--o{ ASSET : holds

    GAME ||--|{ TEAM : has
    TEAM ||--|{ PARTICIPANT : has
    PARTICIPANT ||--o| ARMYSNAPSHOT : brings
    GAME }o--|| PROPERTIES : "pinned by hash"
    GAME ||--o| TABLESTATE : "setup result"
    GAME ||--o{ GAMEEVENT : "append-only log"

    TABLESTATE ||--o{ TERRAINPLACEMENT : holds
    TABLESTATE ||--o{ MARKERPLACEMENT : holds
    TABLESTATE ||--o{ DEPLOYMENTREGION : holds
    TABLESTATE }o..o| ASSET : "texture, or a built-in"
    TERRAINPLACEMENT }o--|| ASSET : mesh
    ASSET ||--o{ VOLUME : "terrain profile"
    TERRAINPLACEMENT ||--o{ VOLUME : overrides
    MARKERPLACEMENT }o..o| ASSET : "visual, or a built-in"
    DEPLOYMENTREGION }o--|| TEAM : "assigned to"

    ACCOUNT {
        uuid id
        string name
    }
    ARMY {
        uuid id
        string name
        string game_system
        string source
    }
    UNIT {
        string name
        stats stats
        list unit_properties
    }
    MODELSLOT {
        string what_it_is
        stats stats
        list model_properties
        enum base_shape
        units base_dims
        units height
    }
    WEAPON {
        string name
        stats stats
        list weapon_properties
    }
    ASSET {
        uuid id
        string name
        enum kind
        float scale
        rotation orientation
        profile profile
    }
    VOLUME {
        enum kind
        enum footprint
        units offset
        degrees yaw
        units bottom
        units top
        flags declarations
        string notes
    }
    BLOB {
        hash hash
        bytes s3_object
    }
    FOLDER {
        uuid id
        string name
    }
    GAME {
        uuid id
        enum status
        id game_rules
        enum scheduler_shape
        bool warns
    }
    TEAM {
        uuid id
    }
    PARTICIPANT {
        uuid id
    }
    ARMYSNAPSHOT {
        uuid id
    }
    PROPERTIES {
        hash content_hash
    }
    GAMEEVENT {
        uuid game_id
        int seq
        string kind
        json payload
    }
    TABLESTATE {
        inches width
        inches length
    }
    TERRAINPLACEMENT {
        units position
        degrees yaw
        float scale
    }
    MARKERPLACEMENT {
        enum kind
        units position
        degrees yaw
        units diameter
        string label
    }
    DEPLOYMENTREGION {
        id style
        list polygons
    }
```

## Reading it

**Solid lines are hard references. Dotted lines are soft ones** — the target may be gone
and nothing breaks. Every dotted line points at `ASSET`, which is the one rule the whole
model is built around: **the rules never touch a mesh.** An army holds the hitbox, an asset
is only ever appearance, so deleting one degrades a slot to a proxy and is never blocked.

**`ASSET` has three profiles**, selected by `kind`. A miniature carries the base its mesh was
made for, which is a search axis rather than a hitbox. Terrain carries collision
volumes and their declarations. A texture is a flat image for the board's top face.

**`BLOB` hangs off `ACCOUNT`, not off the system.** Dedup is per user. Two accounts
uploading identical bytes get two blobs, and nothing ever counts references across accounts.
Participants in a game download each other's assets to render them, which creates no record
on either side — a download, not a share.

**`FOLDER` is the asset library's alone.** It nests, an asset can be in several, and armies
and layouts are flat lists.

**`TABLESTATE` has two owners and one shape.** A game has one as the result of its setup
phase; an account has any number, saved as layouts. There is no separate layout format —
the thing a game ends setup with and the thing a user saves are the same record.

**`ARMYSNAPSHOT` has no edge back to `ARMY`** on purpose. It is a copy taken when the game
loaded, with no live link, so editing a saved army cannot reach into a match in progress.

**`DEPLOYMENTREGION` names a built-in style**, which resolves to a **set** of polygons — one
of OPR's standard styles gives each team two diagonally opposite corners. Measurements are
anchored to table features rather than absolute, so a style works at any board size. Custom
shapes are a later expansion.

**`VOLUME` is an extruded prism** — a circular or rectangular footprint with a bottom and a
top. Convex hulls are gone, so every geometry test is a 2D shape intersection plus a vertical
range. Volumes belong to the terrain asset and are computed from its bounding box at upload,
so they are never absent; a placement may override them. `kind` is `solid` or `area`, and
`declarations` is a closed set of six: impassable, blocks LoS, see in and out, cover,
difficult, dangerous.

**Models are yaw only and always upright**, so a placement is position and yaw. What a model
rests on is derived by settling, never stored.

**Stats are typed and declared at three levels** — unit, model and weapon — mirroring the three
property kinds. A stat declaration carries a kind: target number, distance, count, or dice
expression. `stats` above is a set of declared-stat-to-value pairs, not a free-form bag.

**A weapon lives on the model slot**, resolved in place. There is no shared weapon definition:
two Heavy Rifles in two armies are two copies, because an army holds its own copy of every
value. A melee weapon has no range at all rather than a range of zero.

**`upgrades` and `list_entry` are both gone.** An upgrade's effect is already applied by import
time, so storing it would be two sources for one fact. A source identifier had no reader at all
— the only candidate was a heuristic that was deliberately deferred.

**A ruleset is two things.** `PROPERTIES` is the pinned data — stat definitions plus three
disjoint catalogs, `UnitProperty`, `ModelProperty` and `WeaponProperty`. `GAME.game_rules`
names the per-ruleset C#: turn structure, actions, pipeline order, scheduler, dice. Only the
data half is pinned, which is an open problem for replay. There is no `RulePack` entity and no
registry table.

**Every enumeration is a closed set**, never a free string — `status`, `kind`, `scheduler_shape`
and the geometry enums all have named values in the design docs.

## What is deliberately not here

**In-play state is not persisted.** Unit in play, model in play, placement, wounds,
conditions, the shelf, whose activation it is, the score — none of it is a row. It is
reconstructed in memory by replaying `GAMEEVENT` from `TABLESTATE`, which is the log's
initial state. A table growing a "current position" column means the event spine has been
broken.

That is also why terrain is locked after setup with no flag and no check: terrain lives in
`TABLESTATE`, and nothing in the log can reach it. Making terrain destructible later is
additive — terrain events on top of the same snapshot.

**Built-ins are not rows.** Predefined board textures and deployment zone shapes ship with
the client. Nobody owns them, nothing is copied into an account, and they need no entity.

**Stytch stores nothing here.** No Stytch identifier appears in the model; the only link is
`external_id` on their side holding `ACCOUNT.id`.

**Two schemas, one shared id.** Everything above except `GAMEEVENT` is management's.
`GAMEEVENT` is the whole of the game server's, and there are no joins across the boundary.

## Where these came from

| Cluster | Design |
| --- | --- |
| `ACCOUNT`, `GAME`, `TEAM`, `PARTICIPANT` | [Accounts](superpowers/specs/2026-08-29-accounts-design.md) |
| `ARMY`, `UNIT`, `MODELSLOT` | [The army](superpowers/specs/2026-08-28-army-design.md), [table domain model](superpowers/specs/2026-08-27-table-domain-model-design.md) |
| `ASSET`, `BLOB`, `FOLDER` | [The asset library](superpowers/specs/2026-08-28-asset-library-design.md) |
| `TABLESTATE` and everything under it | [Setup content](superpowers/specs/2026-08-29-setup-content-design.md) |
| `VOLUME`, and every geometric field | [Geometry values](superpowers/specs/2026-08-29-geometry-values-design.md) |
| `GAMEEVENT`, `ARMYSNAPSHOT` | [`architecture.md`](architecture.md) |
| `PROPERTIES`, and every enumeration | [Identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md) |
| `WEAPON`, stats, and property entries | [Content values](superpowers/specs/2026-08-29-content-values-design.md) |
