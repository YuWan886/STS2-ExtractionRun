using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using ExtractionRun.Lifecycle;
using ExtractionRun.Networking;

namespace ExtractionRun.UI;

/// <summary>
/// Hooks the character-select screen for the 搜打撤 launch flow:
/// <list type="bullet">
/// <item>Postfix on the three <c>Initialize*</c> methods: stages the local player's pending carry into the lobby's
/// run saved-data and — when launching from the warehouse hub — applies the extraction modifier to the lobby
/// (broadcast to every machine in MP).</item>
/// <item>Prefix on <c>BeginRun</c>: captures the modifiers the base screen would otherwise drop (its private
/// <c>StartNew*Run</c> passes an empty list to NGame), so <c>ExtractionRunStart</c> can forward them.</item>
/// </list>
/// 挂钩角色选择界面：初始化后暂存待发携带并应用搜打撤修正项；BeginRun 前暂存修正项供 NGame.Start*Run 前向转发。
/// </summary>
public static class CharacterSelectPatch
{
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
    private static class InitSingleplayerPatch
    {
        private static void Postfix(NCharacterSelectScreen __instance)
        {
            InitializeExtractionLobby(__instance, isHost: true);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsHost))]
    private static class InitHostPatch
    {
        private static void Postfix(NCharacterSelectScreen __instance)
        {
            InitializeExtractionLobby(__instance, isHost: true);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsClient))]
    private static class InitClientPatch
    {
        private static void Postfix(NCharacterSelectScreen __instance)
        {
            InitializeExtractionLobby(__instance, isHost: false);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class BeginRunPatch
    {
        private static void Prefix(IReadOnlyList<ModifierModel> modifiers)
        {
            if (modifiers.Count > 0)
            {
                ExtractionRunContext.PendingRunModifiers = modifiers;
            }
        }
    }

    private static void InitializeExtractionLobby(NCharacterSelectScreen screen, bool isHost)
    {
        StartRunLobby lobby = screen._lobby;
        if (lobby == null)
        {
            return;
        }

        // Always stage the local player's persistent pending carry; it is only consumed by the extraction modifier.
        ExtractionCarrySync.StagePendingCarry(lobby, lobby.NetService.NetId);

        // When launching from the warehouse hub (host / singleplayer), apply the extraction modifier to the lobby so
        // it travels into the run on every machine. Clear the launch flag so it doesn't leak into later runs.
        if (ExtractionRunContext.IsExtractionLaunch)
        {
            ExtractionCarrySync.ApplyExtractionModifier(lobby);
            ExtractionRunContext.IsExtractionLaunch = false;
            Entry.Logger.Info("CharacterSelectPatch: extraction modifier applied to lobby.");
        }
        else
        {
            Entry.Logger.Debug("CharacterSelectPatch: character-select opened without an extraction launch.");
        }
    }
}
