using MegaCrit.Sts2.Core.Saves.Runs;
using ExtractionRun.Data;

namespace ExtractionRun.Lifecycle;

/// <summary>
/// How the run ended — drives the settlement screen's header and which sections render.
/// <list type="bullet">
/// <item>Victory: run won, full deposit + clear reward.</item>
/// <item>Defeat: run lost, carried loadout lost (shown for info).</item>
/// <item>ExtractionPoint: the party extracted at the 撤离点 — a defeat for scoring, but the selected loot IS deposited
/// (durability deducted like a victory, no clear reward).</item>
/// </list>
/// 跑局的结束方式——决定结算屏标题与渲染哪些分区：胜利=通关全额入仓+清关奖励；失败=携带装备损失（仅提示）；从撤离点撤离=
/// 按失败计分但选中的战利品正常入仓（耐久按胜利扣，无清关奖励）。
/// </summary>
public enum ExtractionSettlementKind
{
    Defeat,
    Victory,
    ExtractionPoint,
}

/// <summary>
/// The outcome of one extraction run, captured at run end for the post-run settlement screen.
/// <list type="bullet">
/// <item>Success: the loot deposited into the warehouse (final deck minus clones, relics, potions, gold). Each card /
/// relic copy carries its post-extraction durability; copies that broke (1 → 0) are listed separately as 战损.</item>
/// <item>Failure: the carried loadout that was consumed at run start and is now lost (durability as carried — it never
/// decrements on a loss).</item>
/// </list>
/// 一次搜打撤跑局的结算结果：成功=存入仓库的战利品（每份牌/遗物带撤离后的新耐久，战损（1→0）副本单列）；失败=开跑时消耗、
/// 现已损失的那套携带装备（耐久为携带时的值——失败不减耐久）。
/// </summary>
public sealed class ExtractionSettlementResult
{
    /// <summary>How the run ended. 跑局如何结束。</summary>
    public ExtractionSettlementKind Kind { get; set; } = ExtractionSettlementKind.Defeat;

    /// <summary>True = extracted successfully (run won and local player alive); false = lost. 是否撤离成功。</summary>
    public bool Success { get; set; }

    /// <summary>Cards shown by the settlement screen (deposited on success, lost on failure). 结算展示的卡牌。</summary>
    public List<WarehouseCard> Cards { get; set; } = new();

    public List<WarehouseRelic> Relics { get; set; } = new();

    /// <summary>
    /// Cards that broke on extraction (a carried copy at durability 1 → 0) and were NOT deposited — 战损, shown for info
    /// only. Success only. 撤离时耐久耗尽（携带 1 耐久 → 0）未被存入的卡牌——战损，仅提示展示。仅成功时有值。
    /// </summary>
    public List<WarehouseCard> BrokenCards { get; set; } = new();

    /// <summary>Relics that broke on extraction (same 战损 semantics). 撤离时耐久耗尽的遗物（同战损语义）。</summary>
    public List<WarehouseRelic> BrokenRelics { get; set; } = new();

    /// <summary>
    /// Expired relics (used up / melted) that were NOT deposited — dropped on extraction and only shown for info.
    /// Success only. 失效遗物（用尽/融化），撤离时丢弃、不入库，仅在结算屏提示展示。仅成功时有值。
    /// </summary>
    public List<SerializableRelic> ExpiredRelics { get; set; } = new();

    public List<SerializablePotion> Potions { get; set; } = new();

    /// <summary>Gold shown by the settlement screen (deposited on success, lost on failure). 结算展示的金币。</summary>
    public int Gold { get; set; }
}

/// <summary>
/// Transient process-wide holder for the most recent extraction settlement result. Set by <c>ExtractionRunEnd</c> at
/// run end, cleared when the next extraction run starts, read by the game-over settlement button. 最近的撤离结算结果暂存。
/// </summary>
public static class ExtractionSettlement
{
    public static ExtractionSettlementResult? Current { get; set; }

    public static void Clear()
    {
        Current = null;
    }
}
