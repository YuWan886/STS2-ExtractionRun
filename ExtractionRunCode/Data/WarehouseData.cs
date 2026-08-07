using System.Collections.Generic;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Data;

/// <summary>
/// Persistent per-profile stash for the 搜打撤 (Search-Loot-Extract) mode.
/// Cards/relics/potions are stored in their serializable form so upgrades, enchantments and saved properties survive
/// across runs. Stored in <c>ModDataStore</c> with <c>SaveScope.Profile</c> (one warehouse per save slot / player).
/// 搜打撤模式的持久仓库（每个存档位一份）。卡牌/遗物/药水以可序列化形式存储，跨局保留升级、附魔与属性。
/// </summary>
public sealed class WarehouseData
{
    /// <summary>Whether the first-time seed (starter/common cards + relics + 1000 gold) has been granted. 是否已发放首次种子。</summary>
    public bool Seeded { get; set; }

    /// <summary>Stored cards, each an independent copy (duplicates allowed). 仓库中的卡牌（允许重复）。</summary>
    public List<SerializableCard> Cards { get; set; } = new();

    /// <summary>Stored relics (duplicates allowed for stackable relics). 仓库中的遗物。</summary>
    public List<SerializableRelic> Relics { get; set; } = new();

    /// <summary>Stored potions. 仓库中的药水。</summary>
    public List<SerializablePotion> Potions { get; set; } = new();

    /// <summary>Stored gold. 仓库中的金币。</summary>
    public int Gold { get; set; }
}
