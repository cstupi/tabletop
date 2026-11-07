using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The waiting panel's Join: what it says, whether it can be pressed, and why not. Leave: it gives up a team I hold (LeaveTeam), else it
/// takes the team (PickTeam), one more team when I already play one.</summary>
public sealed record TeamChoice(Guid TeamId, string Text, bool Enabled, string Why, bool Leave = false)
{
    public LobbyEdit Edit => Leave ? new LeaveTeam(TeamId) : new PickTeam(TeamId);
}
/// <summary>One army picker: the team it brings an army to (null for a participant with no team), its label, the army picked.</summary>
public sealed record ArmySeat(Guid? TeamId, string Label, Guid? ArmyId)
{
    public PickArmy Pick(Guid? armyId) => new(armyId, TeamId);
}
/// <summary>One army in a picker. An army of another edition than the game's is listed disabled, with the reason (rule-set versions spec → A 3.5
/// army in a 3.6 game, and the reverse); the game server refuses it either way.</summary>
public sealed record ArmyOption(ArmySummary Army, string? DisabledReason)
{
    public string Label => LobbyView.ArmyLabel(Army);
    public bool Enabled => DisabledReason is null;
    /// <summary>The picker's text: the label, and the reason when disabled.</summary>
    public string Text => DisabledReason is null ? Label : $"{Label} · {DisabledReason}";
}

/// <summary>
/// The lobby's army pickers and words shared by the lobby, setup and the create dialog. Every seat brings its own army, so a team may hold several
/// (rulebook p.50 Team Games). The team columns are TeamColumns.
/// </summary>
public static class LobbyView
{
    /// <summary>An army in a picker: its name and the edition it was imported under.</summary>
    public static string ArmyLabel(ArmySummary a) => $"{a.Name} ({a.Edition})";

    public const string ReimportReason = "3.5 list: re-import it from Army Forge on the website", NoLongerServedReason = "3.6 list: Army Forge no longer serves 3.5 lists";

    /// <summary>A picker's armies for a game: the game's edition first, the rest disabled with the reason.</summary>
    public static IReadOnlyList<ArmyOption> ArmyOptions(GameInfo game, IReadOnlyList<ArmySummary> armies) => ArmyOptions(game.GameRules, armies);

    /// <summary>A picker's armies for a game on <paramref name="gameRules"/> (the create dialog's selected set). A rule set this build does not
    /// ship disables nothing.</summary>
    public static IReadOnlyList<ArmyOption> ArmyOptions(string gameRules, IReadOnlyList<ArmySummary> armies)
    {
        var edition = RuleSets.All.Contains(gameRules) ? RuleSets.EditionOf(gameRules) : null;
        return armies.Select(a => new ArmyOption(a, edition is null || a.Edition == edition ? null : a.Edition == ArmyEditions.V35 ? ReimportReason : NoLongerServedReason))
            .OrderBy(o => o.Enabled ? 0 : 1).ToList();
    }

    public const string CreatorOnly = "Only the game's creator changes the configuration.";

    /// <summary>My army picker: "Your army", or one per team I hold ("Your army for Team 2"), or one for my teamless row. Each seat picks its own;
    /// a teammate's army is theirs.</summary>
    public static IReadOnlyList<ArmySeat> Armies(GameInfo game, Guid? participantId)
    {
        var seats = Seats.Mine(game, participantId);
        var teamed = game.Teams.OrderBy(t => t.Ordinal).SelectMany(t => seats.Where(s => s.TeamId == t.Id).Select(s => (Team: t, Seat: s))).ToList();
        if (teamed.Count == 0) return seats.Take(1).Select(s => new ArmySeat(null, "Your army", s.ArmyId)).ToList();
        return teamed.Select(x => new ArmySeat(x.Team.Id, teamed.Count == 1 ? "Your army" : $"Your army for Team {x.Team.Ordinal}", x.Seat.ArmyId)).ToList();
    }

    /// <summary>The End setup button: the game server's DropOffSlab rule removes each piece whose centre is off the table (edge inclusive).</summary>
    public static string EndSetupText(TableState? draft)
    {
        var off = draft is { } d ? d.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(d.Size, t.Position.Horizontal)) : 0;
        return off switch
        {
            0 => "End setup",
            1 => "End setup (1 piece off the table will be removed)",
            _ => $"End setup ({off} pieces off the table will be removed)",
        };
    }
}
