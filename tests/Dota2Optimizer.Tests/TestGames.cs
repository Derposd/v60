using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Tests;

internal static class TestGames
{
    public static IReadOnlyDictionary<GameId, SteamInfo> One(SteamInfo steam) => new Dictionary<GameId, SteamInfo> { [steam.Game] = steam };
}
