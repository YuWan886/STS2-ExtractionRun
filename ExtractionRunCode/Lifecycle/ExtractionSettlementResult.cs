using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Lifecycle;

/// <summary>
/// The outcome of one extraction run, captured at run end for the post-run settlement screen.
/// <list type="bullet">
/// <item>Success: the loot deposited into the warehouse (final deck minus clones, relics, potions, gold).</item>
/// <item>Failure: the carried loadout that was consumed at run start and is now lost.</item>
/// </list>
/// 一次搜打撤跑局的结算结果：成功=存入仓库的战利品；失败=开跑时消耗、现已损失的那套携带装备。
/// </summary>
public sealed class ExtractionSettlementResult
{
    /// <summary>True = extracted successfully (run won and local player alive); false = lost. 是否撤离成功。</summary>
    public bool Success { get; set; }

    /// <summary>Cards shown by the settlement screen (deposited on success, lost on failure). 结算展示的卡牌。</summary>
    public List<SerializableCard> Cards { get; set; } = new();

    public List<SerializableRelic> Relics { get; set; } = new();

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
