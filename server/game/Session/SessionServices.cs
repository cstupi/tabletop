using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Rules;

namespace Tabletop.GameServer.Session;

public sealed record SessionServices(IGameRows Rows, IEventLog Log, IRulesEngineFactory Engines, GameServerOptions Options, ILoggerFactory Logging);
