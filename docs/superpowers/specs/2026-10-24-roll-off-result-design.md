# The roll-off result

Owner, 2026-10-24: *"We should show a temporary message on screen when a team wins the turn order roll off so its clearer
which team goes first. We dont need the continue button when determining sides."*

## No gate

The roll-off (`GfRolls.RollOffSequence`, read by `Scheduler.RollOff`) opens an every-team Confirm between its roll and its
ranking step whatever the wait setting says, so every player presses Continue before deployment starts. That gate goes: the
roll-off runs its roll and its ranking in one go and the scheduler reads the order at once. Dice are never edited, so the
gate held nothing. A game whose log stands at that open gate (a roll-off started, unfinished) resumes by running the
ranking step over the dice already rolled; replay of a finished roll-off is unchanged.

## The message

When the roll-off resolves live, every client shows a message over the table for 4 s, in the turn banner's style
(`TurnBanner`): **"<team> goes first"**, with each team's roll under it, best first ("Team 1 rolled 5 · Team 2 rolled 3"),
ties and re-rolls as the roll-off resolved them. The team names are the ones the header uses. It shows whatever the Turn
banner setting says, and not again on a reconnect or a replay seek. `Core` reads it from the roll-off's frame in the log
(the ranking step's `place_1..place_n` and the roll's dice), tested without Godot.
