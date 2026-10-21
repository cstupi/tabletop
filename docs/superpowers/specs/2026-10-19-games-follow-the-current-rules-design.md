# Games follow the current rules

Owner ruling: "If a rule FIX is released it shouldnt publish a new version. everything should move to it.. its a fix"

Every game that has not ended runs the `Properties` set the running game server seeded for its game rules. An ended game
keeps the set it ended on, for replay.

## The current set per game rules

`PropertiesSeeder.SeedEmbeddedAsync` records, per `GameRules`, the hash it seeded in the singleton `CurrentProperties`
(`server/game/Properties/CurrentProperties.cs`: `For(gameRules)`, `Set(gameRules, hash)`). Seeding runs only at startup, so a
released set always arrives with a restart, and every session hydrates again. In the `Test` environment the seeder does not
run, `For` returns null and no game moves.

## Repin at hydration

`Hydrator.HydrateAsync`, after `ReadGameAsync`: when the game is not `ended` and `CurrentProperties.For(info.GameRules)` names
another hash, `IGameRows.RepinPropertiesAsync(gameId, hash)` writes it
(`update management.game set properties_hash = $2 where id = $1 and status <> 'ended'`) and hydration continues with that hash.
The engine, `Welcome` and the client, which fetches rule texts by the game's hash, all read the current set. One Information
log line names the game and the old and new hash.

## Grants survive the move

A `Grant.EffectId` names an effect row of the set that minted it, and effect ids are minted per set. A loaded `PropertySet`
carries aliases: an effect row of another set of the same game rules stands for this set's row with the same entry, the same
position path (the index among siblings at each level from the top-level effect down: `effect.position` through
`parent_effect_id`; in memory the index in `CatalogEntry.Effects` and `Effect.Children`) and the same kind
(`EffectAliases.Match`). No match, no alias, and the grant acts as a grant naming no row does: not at all.
`PropertySet.EffectById` falls back to the aliases, so `Lookup`, `LiveProperties` and `MenuLimits` read old grants unchanged.
`PostgresPropertiesStore.LoadAsync` reads every effect row of the other sets of the game rules (rows on a null `position` path
are left out); `InMemoryPropertiesStore` aliases its own other sets the same way.

## Replay

Replay applies recorded dice and results, so a log written under the old set replays under the current one; a step list or
effect that changed acts from the move on.
