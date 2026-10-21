using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;

namespace Tabletop.GameServer.Session;

/// <summary>Current: each game rules' current set, which hydration moves a game that has not ended to; null moves nothing.</summary>
public sealed record SessionServices(IGameRows Rows, IEventLog Log, IRulesEngineFactory Engines, GameServerOptions Options, ILoggerFactory Logging,
    CurrentProperties? Current = null);
