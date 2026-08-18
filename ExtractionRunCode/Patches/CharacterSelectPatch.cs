using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Networking;
using ExtractionRun.UI;

namespace ExtractionRun.Patches;

/// <summary>
/// Hooks the character-select screen for the 搜打撤 launch flow:
/// <list type="bullet">
/// <item>Postfix on the three <c>Initialize*</c> methods: stages the local player's pending carry into the lobby's
/// run saved-data and — when launching from the warehouse hub — applies the extraction modifier to the lobby
/// (broadcast to every machine in MP).</item>
/// <item>Prefix on <c>BeginRun</c>: captures the modifiers the base screen would otherwise drop (its private
/// <c>StartNew*Run</c> passes an empty list to NGame), so <c>ExtractionRunStart</c> can forward them.</item>
/// <item>Prefix on <c>OnSubmenuClosed</c>: an abandoned extraction lobby never started a run, so the confirmed carry it
/// staged is wiped — it would otherwise leak into a later hub open (e.g. a loadout confirmed in multiplayer showing up
/// in the singleplayer hub).</item>
/// </list>
/// 挂钩角色选择界面：初始化后暂存待发携带并应用搜打撤修正项；BeginRun 前暂存修正项供 NGame.Start*Run 前向转发；
/// 退出搜打撤大厅时清空已暂存的待发携带。
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
        private static void Prefix(ref IReadOnlyList<ModifierModel> modifiers)
        {
            if (modifiers.Count > 0)
            {
                ExtractionRunContext.PendingRunModifiers = modifiers;
                // The base game only ever ignores this parameter: it logs "Modifiers list is not empty while starting
                // a standard run" + a full stack trace, then passes an empty array to StartNew*Run. We already captured
                // the list for ExtractionRunStart to restore at NGame.Start*Run, so hand the original an empty list to
                // keep that error out of the log. The LobbyBeginRunMessage (MP) is built in BeginRunForAllPlayers before
                // this runs, so clients still receive the modifier from the message.
                // 基础游戏只会忽略该参数：打印「standard run 修正项列表非空」+ 完整堆栈，再向 StartNew*Run 传空数组。修正项
                // 已暂存供 ExtractionRunStart 在 NGame.Start*Run 恢复，此处把空列表交给原方法以消除这条报错。MP 的
                // LobbyBeginRunMessage 在 BeginRunForAllPlayers 中先于本方法构建，客机仍会从消息里收到修正项。
                modifiers = Array.Empty<ModifierModel>();
            }
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
    private static class OnSubmenuClosedPatch
    {
        /// <summary>
        /// An abandoned extraction lobby (back / disconnect) never started a run, so the carry its player confirmed in
        /// the hub was never consumed — but the lobby it was staged into is gone. Without this, that confirmed loadout
        /// leaks into a later hub open (e.g. a loadout confirmed in multiplayer showing up in the singleplayer hub).
        /// Cleared only when the local player actually staged a carry into THIS lobby: host/singleplayer always stage
        /// at init, while a client who joined an extraction room but never confirmed stages nothing and keeps their old
        /// draft. The run-start path never reaches OnSubmenuClosed (the scene is replaced, not popped), so
        /// <c>AfterRunCreated</c> stays the sole consume-clearing point in that flow.
        /// 退出已确认携带的搜打撤大厅（返回/断线）而未开跑：携带未被消耗，但暂存它的厅已不存在，不清空会泄漏进下次打开仓库
        /// （多人确认的配置出现在之后单机的仓库）。仅当本机确向该厅暂存过携带才清空：主机/单机初始化即暂存；加入搜打撤房间
        /// 但未确认的客机无暂存，保留旧草稿。开跑不会走 OnSubmenuClosed（场景替换而非弹出），消耗清空仍由 AfterRunCreated 独占。
        /// </summary>
        private static void Prefix(NCharacterSelectScreen __instance)
        {
            StartRunLobby? lobby = __instance._lobby;
            if (lobby == null || !ExtractionCarrySync.HasExtractionModifier(lobby.Modifiers))
            {
                return;
            }

            if (ExtractionRunData.Carry.Lobby.TryGet(lobby, lobby.NetService.NetId, out _))
            {
                PendingCarryStore.Clear();
                Entry.Logger.Info("CharacterSelectPatch: abandoned extraction lobby — cleared the staged pending carry.");
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

        if (ExtractionRunContext.IsExtractionLaunch)
        {
            // Consume the intent first: even if the modifier apply failed, a later vanilla launch must not inherit it.
            ExtractionRunContext.IsExtractionLaunch = false;
            ExtractionCarrySync.StagePendingCarry(lobby, lobby.NetService.NetId);
            ExtractionCarrySync.ApplyExtractionModifier(lobby);

            // Host-authoritative 撤离点 settings: the host broadcasts them right after applying the modifier, so every
            // client already in the lobby has them before the run starts (the all-ready gate holds the run until then).
            // 撤离点主机权威设置：主机在应用修正项后随即广播，已在厅内的客机在开跑前（全员就绪门）即持有该值。
            ExtractionPointSettingsSync.BroadcastSettings(lobby.NetService);

            // The run seed (host/singleplayer only): injected before BeginRun so the host's begin-run message carries
            // it to every machine — overriding the seed in NGame.Start*Run would desync (clients use the message seed).
            // 注入跑局种子（仅主机/单机）：在 BeginRun 之前写入大厅，主机 begin-run 消息随之把它带给所有机器——若改在
            // NGame.Start*Run 覆盖种子会造成不同步（客户端用的是 begin-run 消息里的种子）。
            if (isHost && ExtractionRunContext.PendingSeed is { } seed)
            {
                ExtractionRunContext.PendingSeed = null;
                lobby.SetSeed(seed);
                Entry.Logger.Info($"CharacterSelectPatch: applied run seed {seed} to extraction lobby.");
            }

            // A lobby-flow host created the room before any warehouse-hub setup, so force the carry-config modal now
            // (reuses the client modal semantics: confirm re-stages the carry into the lobby, back pops the
            // character-select screen which disconnects the host session and deletes the room).
            // 联机大厅建房流里主机没机会先配置携带，此处强制弹出携带配置模态（复用客机模态语义：确认重暂存携带进大厅，
            // 返回弹出角色选择屏断开主机会话并删除房间）。
            if (ExtractionRunContext.HostCarrySetupRequired)
            {
                ExtractionRunContext.HostCarrySetupRequired = false;
                OpenCarrySetupModal(screen, lobby);
            }

            Entry.Logger.Info("CharacterSelectPatch: extraction modifier applied to lobby.");
        }
        else if (!isHost && ExtractionCarrySync.HasExtractionModifier(lobby.Modifiers))
        {
            // Client joined an extraction room: force the warehouse hub so they configure a carry before the lobby.
            // The lobby already carries the host's modifier (join response → InitializeFromMessage), so detection is
            // synchronous here. The carry is staged into the lobby ONLY on confirm (ConfirmCarryForClient), not here:
            // staging the pre-edit pending value before the client confirms would push the client's OLD carry to the
            // host, and if the confirm's re-stage push fails (RitsuLib trailer / character-null guard) the run would
            // consume that stale carry instead of what the client actually confirmed — a swallowed-items dupe.
            // 客机加入搜打撤房间：强制打开仓库界面，配置完成后才能进入大厅。携带只在该模态「确认」时暂存进大厅
            // （ConfirmCarryForClient），此处不暂存——否则会把确认前的旧携带推到主机，若确认重暂存推送失败，开跑
            // 就会消耗这份陈旧携带而非客机真正确认的内容，造成物资被吞。
            // A late-joining client may have missed the host's settings broadcast — ask for a copy now (the run can't
            // start until the client confirms, so the reply always lands before act generation). 后加入的客机可能错过
            // 主机广播——此刻请求一份（客机确认前开跑不会发生，应答必然早于章节生成落地）。
            ExtractionPointSettingsSync.RequestFromHost(lobby.NetService);
            OpenCarrySetupModal(screen, lobby);
        }
        else
        {
            Entry.Logger.Debug("CharacterSelectPatch: character-select opened without an extraction launch.");
        }
    }

    /// <summary>
    /// Forces the warehouse hub as a modal over the character-select screen. Used by a client who joined an extraction
    /// room and — in the STS2-Game-Lobby flow — by the host who created one (the room was already published before any
    /// hub setup). The modal's confirm persists the draft and re-stages it into the lobby; back pops the screen.
    /// 在角色选择屏上强制弹出仓库配置模态：客机加入搜打撤房间时；以及联机大厅建房流的主机（房间已发布、主机尚未配置携带）。
    /// 模态确认持久化草稿并重暂存进大厅；返回弹出该屏。</summary>
    private static void OpenCarrySetupModal(NCharacterSelectScreen screen, StartRunLobby lobby)
    {
        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        var hub = new WarehouseHubScreen(screen._stack, null, WarehouseHubScreen.HubMode.MultiplayerClient, lobby);
        game.AddChild(hub);
        Entry.Logger.Info("CharacterSelectPatch: showing carry setup modal over character select.");
    }
}
