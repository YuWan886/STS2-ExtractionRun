using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace ExtractionRun.Lifecycle;

/// <summary>
/// Forwards the extraction modifier from the character-select screen into the run creation path.
/// <c>NCharacterSelectScreen</c> receives the lobby's modifiers in <c>BeginRun</c> but its private
/// <c>StartNewSingleplayerRun/StartNewMultiplayerRun</c> drop them and pass an empty list to
/// <c>NGame.StartNewSingleplayerRun/StartNewMultiplayerRun</c>. We capture the modifiers in a
/// <c>NCharacterSelectScreen.BeginRun</c> prefix and restore them in the <c>NGame.Start*Run</c> prefixes (which DO
/// forward modifiers into <c>RunState.CreateForNewRun</c>). The run is also forced to <c>GameMode.Custom</c> so
/// achievements/epochs are locked for the extraction mode.
/// 把修正项从角色选择界面转发进跑局创建路径：BeginRun 前缀暂存，NGame.Start*Run 前缀恢复并强制 Custom 模式。
/// </summary>
public static class ExtractionRunStart
{
    /// <summary>Harmony <c>NGame.StartNewSingleplayerRun</c> prefix: restore stashed modifiers and force Custom mode.</summary>
    [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
    [HarmonyPriority(Priority.First)]
    private static class StartSingleplayerPrefix
    {
#if STS2_API_0_113_OR_LATER
        private static void Prefix(ref IReadOnlyList<ModifierModel> modifiers, ref bool isCustomGame)
        {
            if (ExtractionRunContext.PendingRunModifiers is { } pending)
            {
                modifiers = pending;
                ExtractionRunContext.PendingRunModifiers = null;
                ExtractionRunContext.PendingSeed = null;
                isCustomGame = true;
            }
        }
#else
        private static void Prefix(ref IReadOnlyList<ModifierModel> modifiers, ref GameMode gameMode)
        {
            if (ExtractionRunContext.PendingRunModifiers is { } pending)
            {
                modifiers = pending;
                ExtractionRunContext.PendingRunModifiers = null;
                ExtractionRunContext.PendingSeed = null;
                gameMode = GameMode.Custom;
            }
        }
#endif
    }

    /// <summary>Harmony <c>NGame.StartNewMultiplayerRun</c> prefix: restore stashed modifiers and force Custom mode.</summary>
    [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
    [HarmonyPriority(Priority.First)]
    private static class StartMultiplayerPrefix
    {
        private static void Prefix(StartRunLobby lobby, ref IReadOnlyList<ModifierModel> modifiers)
        {
            if (ExtractionRunContext.PendingRunModifiers is { } pending)
            {
                modifiers = pending;
                ExtractionRunContext.PendingRunModifiers = null;
                ExtractionRunContext.PendingSeed = null;

                PropertyInfo? gameModeProperty = typeof(StartRunLobby).GetProperty(nameof(StartRunLobby.GameMode));
                if (gameModeProperty != null)
                {
#if STS2_API_0_113_OR_LATER
                    gameModeProperty.SetValue(lobby, GameMode.Standard);
                    typeof(StartRunLobby).GetProperty(nameof(StartRunLobby.IsCustom))?.SetValue(lobby, true);
#else
                    gameModeProperty.SetValue(lobby, GameMode.Custom);
#endif
                }
            }
        }
    }
}
