using System.Linq;
using BepInEx.Bootstrap;

namespace NoCompanyPenalty.Dependencies;

internal static class DependencyChecker {
    internal static bool IsLobbyCompatibilityInstalled() =>
        Chainloader.PluginInfos.Values.Any(metadata => metadata.Metadata.GUID.Contains("LobbyCompatibility"));
}