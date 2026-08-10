using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace ExtractionRun.Compatibility;

/// <summary>
/// Some hosts guard the standard-mode character-select lobby against seed changes:
/// <c>NCharacterSelectScreen.SeedChanged</c> throws <c>NotImplementedException</c>. The 搜打撤 launch flow sets the
/// run seed on that lobby via <c>StartRunLobby.SetSeed</c> — the seed is written and broadcast before the listener
/// call, so only the throw needs skipping (the screen renders no seed UI). The guard's presence is probed from the
/// method IL at runtime, so the fix adapts to whatever host version carries it instead of hardcoding a version
/// threshold. Installed on every machine (host and clients): <c>SetSeed</c> broadcasts <c>LobbySeedChangedMessage</c>,
/// and the client's listener callback hits the same throw.
/// 兼容性修复：部分主机在标准模式角色选择大厅上禁止修改种子（SeedChanged 抛 NotImplementedException）。搜打撤发起
/// 流程需要在该大厅设置种子，故跳过该抛出（种子已在监听器回调前写入大厅并广播）。由 IL 探测决定，只对存在该守卫的主机生效。
/// 所有机器都要装（主机与客户端）：SetSeed 广播 LobbySeedChangedMessage，客户端监听回调同样会撞上该抛出。
/// </summary>
internal static class SeedChangeCompat
{
    /// <summary>True when the host's <c>NCharacterSelectScreen.SeedChanged</c> contains a <c>throw</c>.</summary>
    public static bool HostRejectsSeedChanges()
    {
        MethodInfo? method = typeof(NCharacterSelectScreen)
            .GetMethod(nameof(NCharacterSelectScreen.SeedChanged), BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
        {
            return false;
        }

        try
        {
            return method.GetMethodBody()?.GetILAsByteArray() is { } il && Array.IndexOf(il, (byte)0x7A) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Harmony prefix that skips the base throw; the character-select screen has no seed UI to update.</summary>
    public static bool SkipSeedChanged() => false;

    /// <summary>Installs the skip patch only when the host rejects seed changes on this screen.</summary>
    public static void InstallIfNeeded(Harmony harmony)
    {
        if (!HostRejectsSeedChanges())
        {
            return;
        }

        MethodInfo? target = typeof(NCharacterSelectScreen)
            .GetMethod(nameof(NCharacterSelectScreen.SeedChanged), BindingFlags.Public | BindingFlags.Instance);
        MethodInfo prefix =
            typeof(SeedChangeCompat).GetMethod(nameof(SkipSeedChanged), BindingFlags.Static | BindingFlags.Public)!;
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        Entry.Logger.Info("SeedChangeCompat: host rejects seed changes on character select; installed skip patch.");
    }
}
