using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Data;

/// <summary>
/// The loadout a player carries into one extraction run: cards and relics bounded by the active carry budget (the
/// capacity pool by default — cards by rarity weight, relics flat; the OFF mode uses <see cref="ExtractionSettings.MaxCarryCards"/>
/// / <see cref="ExtractionSettings.MaxCarryRelics"/> count caps), potions up to the player's potion slots, and a gold
/// amount. Carried items are consumed from the warehouse when the run starts (Tarkov-style: dying or abandoning
/// loses them). Lives both in the run's <c>PlayerRunSavedData</c> (MP-synced) and in the persistent pending store.
/// Card/relic copies carry their durability (as a <see cref="WarehouseCard"/>/<see cref="WarehouseRelic"/> wrapper) so
/// the run data knows what to decrement at extraction; potions and gold have no durability.
/// 一次跑局的携带配置：卡牌与遗物受当前携带预算约束（默认按背包容量池——卡牌按稀有度占格、遗物统一占格；OFF 模式用
/// MaxCarryCards / MaxCarryRelics 数量上限）、不超过药水栏位的药水，以及携带金币。开跑时从仓库消耗。同时存在于局内
/// PlayerRunSavedData（联机同步）与持久待发仓库。牌/遗物副本带耐久（以包装类型携带），供撤离结算递减；药水与金币无耐久。
/// </summary>
public sealed class CarryConfig
{
    /// <summary>Cards carried into the run (≤ MaxCarryCards). 携带的卡牌。</summary>
    public List<WarehouseCard> Cards { get; set; } = new();

    /// <summary>Relics carried into the run (≤ MaxCarryRelics). 携带的遗物。</summary>
    public List<WarehouseRelic> Relics { get; set; } = new();

    /// <summary>Potions carried into the run (≤ potion slots). 携带的药水。</summary>
    public List<SerializablePotion> Potions { get; set; } = new();

    /// <summary>Gold carried into the run (0 = start with no gold). 携带的金币（0 = 无初始金币）。</summary>
    public int Gold { get; set; }

    /// <summary>True when nothing is carried. 未携带任何物品时返回 true。</summary>
    public bool IsEmpty => Cards.Count == 0 && Relics.Count == 0 && Potions.Count == 0 && Gold == 0;
}
