using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace ExtractionRun.Compatibility;

/// <summary>
/// Beta hosts guard the standard-mode character-select lobby against modifier changes:
/// <c>NCharacterSelectScreen.ModifiersChanged</c> throws <c>NotImplementedException</c>. The 搜打撤 launch flow applies
/// the extraction modifier to that lobby via <c>StartRunLobby.SetModifiers</c> — the set + broadcast already happen
/// before the listener call, so only the throw needs skipping (the screen renders no modifier UI). The guard's
/// presence is probed from the method IL at runtime, so the fix adapts to whatever host version carries it instead of
/// hardcoding a version threshold.
/// 兼容性修复：beta 主机在标准模式角色选择大厅上禁止修改修正项（ModifiersChanged 抛 NotImplementedException）。搜打撤发起
/// 流程需要在该大厅设置修正项，故跳过该抛出（修正项已在监听器回调前写入大厅并广播）。是否跳过由 IL 探测决定，只对存在该守卫的主机生效。
/// </summary>
internal static class ModifierChangeCompat
{
    /// <summary>True when the host's <c>NCharacterSelectScreen.ModifiersChanged</c> contains a <c>throw</c>.</summary>
    public static bool HostRejectsModifierChanges()
    {
        MethodInfo? method = typeof(NCharacterSelectScreen)
            .GetMethod(nameof(NCharacterSelectScreen.ModifiersChanged), BindingFlags.Public | BindingFlags.Instance);
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

    /// <summary>Harmony prefix that skips the base throw; the character-select screen has no modifier UI to update.</summary>
    public static bool SkipModifiersChanged() => false;

    /// <summary>Installs the skip patch only when the host rejects modifier changes on this screen.</summary>
    public static void InstallIfNeeded(Harmony harmony)
    {
        if (!HostRejectsModifierChanges())
        {
            return;
        }

        MethodInfo? target = typeof(NCharacterSelectScreen)
            .GetMethod(nameof(NCharacterSelectScreen.ModifiersChanged), BindingFlags.Public | BindingFlags.Instance);
        MethodInfo prefix =
            typeof(ModifierChangeCompat).GetMethod(nameof(SkipModifiersChanged), BindingFlags.Static | BindingFlags.Public)!;
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        Entry.Logger.Info("ModifierChangeCompat: host rejects modifier changes on character select; installed skip patch.");
    }
}
