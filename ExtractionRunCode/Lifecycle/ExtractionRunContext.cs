using MegaCrit.Sts2.Core.Models;

namespace ExtractionRun.Lifecycle;

/// <summary>
/// Transient process-wide state for the extraction-mode launch flow.
/// <list type="bullet">
/// <item><c>IsExtractionLaunch</c>: set by the warehouse hub before launching a run; consumed by the character-select
/// lobby patch to apply the extraction modifier on the lobby.</item>
/// <item><c>PendingRunModifiers</c>: the modifiers captured by the <c>NCharacterSelectScreen.BeginRun</c> prefix and
/// forwarded to <c>NGame.StartNewSingleplayerRun/StartNewMultiplayerRun</c>, which is where the base game would
/// otherwise drop them (it passes an empty array).</item>
/// </list>
/// 搜打撤发起跑局时的临时进程状态：是否由仓库大厅发起、以及从 BeginRun 暂存、转发到 NGame.Start*Run 的修正项。
/// </summary>
public static class ExtractionRunContext
{
    /// <summary>True while the warehouse hub is launching a 搜打撤 run. 由仓库大厅发起跑局时为 true。</summary>
    public static bool IsExtractionLaunch { get; set; }

    /// <summary>Modifiers captured from <c>NCharacterSelectScreen.BeginRun</c>, awaiting forwarding to NGame. 待前向转发的修正项。</summary>
    public static IReadOnlyList<ModifierModel>? PendingRunModifiers { get; set; }

    /// <summary>Seed chosen in the warehouse hub for the next extraction run (host/singleplayer only); null = random.
    /// Consumed into the lobby by <c>CharacterSelectPatch</c> before the run begins. 仓库大厅为下一局设置的种子（仅主机/单机）；
    /// null 表示随机。开跑前由 CharacterSelectPatch 注入大厅。</summary>
    public static string? PendingSeed { get; set; }

    /// <summary>
    /// Challenge ids selected in the warehouse hub for the next extraction run (host/singleplayer only); null/empty =
    /// a normal run. Consumed into the modifier by <c>CharacterSelectPatch</c> before the run begins (a session-only
    /// handoff, like <see cref="PendingSeed"/> — never persisted with the carry). 仓库大厅为下一局选定的挑战 id（仅主机/单机）；
    /// 空为普通跑局。开跑前由 CharacterSelectPatch 写入 modifier（仅会话瞬态，同 PendingSeed——不随携带持久化）。
    /// </summary>
    public static IReadOnlyList<string>? PendingChallenges { get; set; }

    /// <summary>
    /// Set only by the STS2-Game-Lobby compat when the host creates an extraction room: the room's ENet host and
    /// lobby are already live before the character-select init, so the host has had no chance to configure a carry in
    /// the warehouse hub. Consumed by <c>CharacterSelectPatch</c> right after it stages the pending carry and applies
    /// the modifier, which then forces the carry-config modal over the character-select screen. The base host-submenu
    /// flow never sets this (the hub ran before launch).
    /// 仅由联机大厅兼容在主机创建搜打撤房间时置位：房间的 ENet host 与大厅在角色选择初始化前就已就绪，主机没机会先在仓库配置携带。
    /// 由 CharacterSelectPatch 在暂存携带并应用修正项后消费，随即在角色选择屏上强制弹出携带配置模态。主菜单主机子菜单流程不会置位
    /// （该流程先开仓库再发起）。
    /// </summary>
    public static bool HostCarrySetupRequired { get; set; }

    /// <summary>Clears the launch flow state (after a run starts or the flow is cancelled).</summary>
    public static void Clear()
    {
        IsExtractionLaunch = false;
        PendingRunModifiers = null;
        PendingSeed = null;
        PendingChallenges = null;
        HostCarrySetupRequired = false;
    }
}
