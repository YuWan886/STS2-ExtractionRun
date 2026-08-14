using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Networking;

namespace ExtractionRun.Modifier;

/// <summary>Which 撤离点 option the local player executed — 普通撤离 (capacity-limited) or 金币撤离 (pay to carry all).</summary>
public enum ExtractionPointKind
{
    Normal,
    Gold,
}

/// <summary>
/// The local player's extraction selection, recorded at the 撤离点 panel confirm and read back by the run-end
/// settlement. Transient per-machine state: the panel records it, <c>ExtractionRunEnd</c> deposits from it, and
/// <c>AfterRunCreated</c> clears it (a stale result must never leak into the next run). For 普通撤离 the maps hold the
/// selected copy counts per id; for 金币撤离 they stay empty and the settlement carries EVERYTHING (minus the fee).
/// 撤离点面板确认时记录的带出选择，跑局结束结算据此入仓。每机瞬态：面板记录、结算读取、开跑时清空。普通撤离记录各 id 的
/// 选中份数；金币撤离不记录（结算全带，仅扣费用）。
/// </summary>
public sealed class ExtractionPointSelection
{
    public ExtractionPointKind Kind { get; init; }

    /// <summary>Selected deck copies per id (普通撤离 only — 金币撤离 takes the whole deck). 选中的牌组份数（仅普通撤离）。</summary>
    public Dictionary<ModelId, int> Cards { get; } = new();

    /// <summary>Selected relics per id (普通撤离 only — 金币撤离 takes all relics). 选中的遗物份数（仅普通撤离）。</summary>
    public Dictionary<ModelId, int> Relics { get; } = new();

    /// <summary>金币撤离 fee to deduct from the deposited gold (0 for 普通撤离). 金币撤离需从入仓金币中扣除的费用。</summary>
    public int GoldFee { get; init; }
}

/// <summary>
/// Transient process-wide state for the 撤离点 extraction flow. Tracks whether the party chose to extract (via the
/// shared event vote) and the local player's selection; after the local panel confirms, waits until EVERY player's
/// machine has confirmed (a net-message barrier) before ending the run, so no machine reaches game-over while a
/// teammate is still picking. Cleared in <c>ExtractionModifier.AfterRunCreated</c>.
/// 撤离点撤离流程的瞬态状态：记录全队是否选择撤离与本地玩家选择；本地面板确认后等待所有机器都确认（网络消息屏障）再结束
/// 跑局，避免一台机器进入结算屏而队友还在挑牌。开跑时清空。
/// </summary>
public static class ExtractionPointFlow
{
    /// <summary>True once the extraction event's option ran to completion (panels confirmed, run ending). 已执行撤离。</summary>
    public static bool IsExtractionChosen { get; set; }

    /// <summary>The local player's recorded selection (null before the panel confirms). 本地玩家记录的选择。</summary>
    public static ExtractionPointSelection? Selection { get; set; }

    private static readonly HashSet<ulong> Confirmed = new();

    /// <summary>The net ids that have confirmed their extraction panel. 已确认撤离面板的玩家 net id 集合。</summary>
    public static IReadOnlyCollection<ulong> ConfirmedPlayers => Confirmed;

    /// <summary>
    /// Called when the local player confirms the panel: records the selection, marks the local machine confirmed and
    /// broadcasts it so every peer knows. 本地面板确认：记录选择、登记本机并广播给所有机器。
    /// </summary>
    public static void NotifyLocalConfirmed(ulong localNetId, ExtractionPointSelection selection)
    {
        Selection = selection;
        IsExtractionChosen = true;
        Confirmed.Add(localNetId);
        ExtractionPointSettingsSync.SendSelectionConfirmed(localNetId);
    }

    /// <summary>Records a remote player's confirm (from the net message). 登记远端玩家确认。</summary>
    public static void HandleRemoteConfirmed(ulong playerNetId)
    {
        Confirmed.Add(playerNetId);
    }

    /// <summary>
    /// Waits until every player has confirmed (or the run is no longer in progress — a disconnect/abandon aborts the
    /// wait). Polls so a peer that never confirms can't hang the party forever if the run ends. 等待所有玩家确认；
    /// 若跑局已结束（断线/放弃）则放弃等待。轮询，避免队友永远不确认时挂死。
    /// </summary>
    public static async Task WaitForAllConfirmed(IReadOnlyList<Player> players)
    {
        HashSet<ulong> all = players.Select(p => p.NetId).ToHashSet();
        while (RunManager.Instance?.IsInProgress == true && !all.IsSubsetOf(Confirmed))
        {
            await Task.Delay(50);
        }
    }

    /// <summary>Resets the transient state when a new run starts (or the flow is abandoned). 开跑时清空瞬态。</summary>
    public static void Clear()
    {
        IsExtractionChosen = false;
        Selection = null;
        Confirmed.Clear();
    }
}
