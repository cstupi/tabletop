namespace Tabletop.Sim.Suite;

/// <summary>The 3.6 army books the 3.6 army games play under, as (uid, army). 3.6 keeps the 3.5 books' uids; Voidspace Stalkers is new in 3.6.</summary>
static class Books36
{
    public static readonly (string Uid, string Army)
        AlienHives = ("w7qor7b2kuifcyvk", "Alien Hives"), BattleBrothers = ("78qp9l5alslt6yj8", "Battle Brothers"),
        BlessedSisters = ("7oi8zeiqfamiur21", "Blessed Sisters"), BloodBrothers = ("xnnqhh1775kvmz2r", "Blood Brothers"),
        BloodPrimeBrothers = ("7ex2x15bpkmy1alv", "Blood Prime Brothers"), ChangeDisciples = ("r6hr29338u4micfw", "Change Disciples"),
        CustodianBrothers = ("ewbra8nv3nq3k27p", "Custodian Brothers"), DaemonsOfChange = ("04z57ua0bwth37zh", "Daemons of Change"),
        DaemonsOfLust = ("9qvy1oufoangt2gl", "Daemons of Lust"), DaemonsOfPlague = ("fmuqw5lr5l6dq0mq", "Daemons of Plague"),
        DaemonsOfWar = ("prsajf5ot936qznk", "Daemons of War"), DaoUnion = ("z8205ez2boggzs22", "DAO Union"),
        DarkBrothers = ("xp5zwh73lg1uaym4", "Dark Brothers"), DarkElfRaiders = ("q11la9v8h1heu9ja", "Dark Elf Raiders"),
        DwarfGuilds = ("fk1mkbp8apvltu0z", "Dwarf Guilds"), ElvenJesters = ("nyh41t82jugcdq8m", "Elven Jesters"),
        EternalDynasty = ("vux1Y5vvULmaxZ8P", "Eternal Dynasty"), GoblinReclaimers = ("k5T0f2XIfpmPO28h", "Goblin Reclaimers"),
        HavocBrothers = ("7o6om21wxlvvy3hq", "Havoc Brothers"), HighElfFleets = ("7i7blhft75q9zfdc", "High Elf Fleets"),
        HumanDefenseForce = ("z65fgu0l29i4lnlu", "Human Defense Force"), HumanInquisition = ("jpc0kyil0juwy602", "Human Inquisition"),
        InfectedColonies = ("dnthspt7c0klhmt8", "Infected Colonies"), Jackals = ("BKi_hJaJflN8ZorH", "Jackals"),
        KnightBrothers = ("w70ha3o85pa7nigq", "Knight Brothers"), LustDisciples = ("drqw1iswxmuugp3d", "Lust Disciples"),
        MachineCults = ("7el7k3hgy5pb9o9i", "Machine Cults"), OrcMarauders = ("1wj1ysgxpuuz9bc7", "Orc Marauders"),
        PlagueDisciples = ("jlray7cwf8mvw5sn", "Plague Disciples"), RatmenClans = ("hk70l4d471plza00", "Ratmen Clans"),
        RebelGuerrillas = ("5wbcv465hacdwvkb", "Rebel Guerrillas"), RobotLegions = ("4k5amkxoybdiqotm", "Robot Legions"),
        SaurianStarhosts = ("U6hwlur14RpnInZr", "Saurian Starhosts"), SoulSnatcherCults = ("zz3kp5ry7ks6mxcx", "Soul-Snatcher Cults"),
        TitanLords = ("3j10zage1lddt6sr", "Titan Lords"), VoidspaceStalkers = ("zpaY1LFqVk-ZJdvW", "Voidspace Stalkers"),
        WarDisciples = ("31xjrm9ivdimkjxp", "War Disciples"), WatchBrothers = ("rvvb3kdn2x2pqkki", "Watch Brothers"),
        WolfBrothers = ("yxjboa8oma9bbdck", "Wolf Brothers");

    /// <summary>Every book above, by uid.</summary>
    public static readonly IReadOnlyDictionary<string, (string Uid, string Army)> ByUid = new[]
    {
        AlienHives, BattleBrothers, BlessedSisters, BloodBrothers, BloodPrimeBrothers, ChangeDisciples, CustodianBrothers, DaemonsOfChange,
        DaemonsOfLust, DaemonsOfPlague, DaemonsOfWar, DaoUnion, DarkBrothers, DarkElfRaiders, DwarfGuilds, ElvenJesters, EternalDynasty,
        GoblinReclaimers, HavocBrothers, HighElfFleets, HumanDefenseForce, HumanInquisition, InfectedColonies, Jackals, KnightBrothers,
        LustDisciples, MachineCults, OrcMarauders, PlagueDisciples, RatmenClans, RebelGuerrillas, RobotLegions, SaurianStarhosts,
        SoulSnatcherCults, TitanLords, VoidspaceStalkers, WarDisciples, WatchBrothers, WolfBrothers,
    }.ToDictionary(b => b.Uid, StringComparer.Ordinal);
}
