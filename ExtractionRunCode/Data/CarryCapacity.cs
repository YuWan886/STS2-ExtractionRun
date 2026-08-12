using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using ExtractionRun.Settings;

namespace ExtractionRun.Data;

/// <summary>
/// The backpack capacity engine (ON mode): cards cost capacity by rarity, relics cost a flat amount, and cards + relics
/// share the same pool — potions and gold are free. Used by the hub (add guard + capacity bar), the gear-code import
/// clamp, and the pending-carry revalidation, so every path reads one rarity→weight mapping.
/// 背包容量引擎（ON 模式）：卡牌按稀有度占容量、遗物统一占额，卡牌与遗物共享同一容量池；药水、金币不计。仓库添加守卫/容量条、
/// 战备码导入钳制与待发携带校验共用这一份稀有度→权重映射，避免各路径口径不一。
/// </summary>
public static class CarryCapacity
{
    /// <summary>True when the capacity system is ON (the unified pool replaces the per-kind count caps).
    /// 容量系统是否开启（统一池取代每类数量上限）。</summary>
    public static bool IsEnabled => ExtractionSettingsPage.Current.CarryCapacityEnabled;

    /// <summary>Total backpack capacity from settings. 设置中的背包总容量。</summary>
    public static int Total => Math.Max(0, ExtractionSettingsPage.Current.CarryCapacity);

    /// <summary>
    /// Capacity cost of one card: Basic/Common share one weight, Uncommon/Rare/Ancient are distinct, and every other
    /// rarity (None/Event/Token/Status/Curse/Quest + unresolvable ids) falls to the Other weight — mirroring the
    /// durability rarity buckets. 一张卡牌占用的容量：基础/普通共用一个权重，罕见/稀有/先古各自独立，其余稀有度
    /// （None/Event/Token/Status/Curse/Quest + 解析不到）落到 Other——与耐久稀有度桶一致。
    /// </summary>
    public static int WeightForCard(ModelId? id)
    {
        CardModel? model = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
        ExtractionSettings settings = ExtractionSettingsPage.Current;
        return model?.Rarity switch
        {
            CardRarity.Basic => settings.CapacityWeightBasicCommon,
            CardRarity.Common => settings.CapacityWeightBasicCommon,
            CardRarity.Uncommon => settings.CapacityWeightUncommon,
            CardRarity.Rare => settings.CapacityWeightRare,
            CardRarity.Ancient => settings.CapacityWeightAncient,
            _ => settings.CapacityWeightOther,
        };
    }

    /// <summary>Capacity cost of one relic (all relics share one value). 一件遗物占用的容量（遗物统一）。</summary>
    public static int WeightForRelic() => Math.Max(1, ExtractionSettingsPage.Current.CapacityWeightRelic);

    /// <summary>Capacity consumed by the carried cards (each copy by its rarity weight). 携带卡牌占用的容量（每份按稀有度权重）。</summary>
    public static int CardCapacity(CarryConfig config)
    {
        int used = 0;
        foreach (WarehouseCard card in config.Cards)
        {
            used += Math.Max(0, WeightForCard(card.Card.Id));
        }

        return used;
    }

    /// <summary>Capacity consumed by the carried relics (flat per copy). 携带遗物占用的容量（每份统一占额）。</summary>
    public static int RelicCapacity(CarryConfig config) => config.Relics.Count * WeightForRelic();

    /// <summary>Total capacity consumed by the given carry (cards + relics). Sums the two per-kind parts, so the hub's
    /// two per-section numbers always add up to this shared-pool used count. 携带已占用的总容量（卡 + 遗物）——即两部分占格
    /// 之和，小节两数相加恒等于共享池占用。</summary>
    public static int UsedCapacity(CarryConfig config) => CardCapacity(config) + RelicCapacity(config);

    /// <summary>Remaining capacity under the given carry. 携带剩余容量。</summary>
    public static int RemainingCapacity(CarryConfig config) => Math.Max(0, Total - UsedCapacity(config));

    /// <summary>
    /// Drops carried copies until the carry fits <paramref name="budget"/> (OFF: per-kind count caps; ON: capacity
    /// pool). The drop order is heaviest-first (fewest drops to free space), ties broken by lowest durability — the
    /// mod's worst-gear-first philosophy applied to the carry instead of the warehouse. Returns the number dropped.
    /// 丢弃携带副本直到符合预算（OFF 每类数量上限 / ON 容量池）。丢弃顺序「先丢最重（最少次数腾出空间）、同重最劣」——把
    /// 最差装备优先哲学从仓库搬用到携带。返回丢弃数。
    /// </summary>
    public static int ClampToBudget(CarryConfig config, CarryBudget budget)
    {
        if (!budget.UsesCapacity)
        {
            // OFF: per-kind count caps. Drop the worst copies first (lowest durability — matching the hub's manual
            // remove and the worst-gear-first theme), so an over-cap carry loses its most expendable copies.
            // OFF：每类数量上限。先丢最劣副本（最低耐久——与仓库手动移除、最差装备优先一致），超限携带损失最可舍弃的副本。
            return DropBelowCount(config.Cards, budget.MaxCards, static c => c.Durability)
                 + DropBelowCount(config.Relics, budget.MaxRelics, static r => r.Durability);
        }

        int used = UsedCapacity(config);
        if (used <= budget.Capacity)
        {
            return 0;
        }

        // Index every carried copy (cards then relics); heaviest first, then lowest durability, then position for a
        // deterministic drop when two copies are indistinguishable.
        var order = new List<(int Index, int Weight, int Durability)>(config.Cards.Count + config.Relics.Count);
        for (int i = 0; i < config.Cards.Count; i++)
        {
            order.Add((i, WeightForCard(config.Cards[i].Card.Id), config.Cards[i].Durability));
        }

        int relicBase = config.Cards.Count;
        for (int i = 0; i < config.Relics.Count; i++)
        {
            order.Add((relicBase + i, WeightForRelic(), config.Relics[i].Durability));
        }

        order.Sort(static (a, b) =>
        {
            int byWeight = b.Weight.CompareTo(a.Weight);
            if (byWeight != 0)
            {
                return byWeight;
            }

            int byDurability = a.Durability.CompareTo(b.Durability);
            return byDurability != 0 ? byDurability : a.Index.CompareTo(b.Index);
        });

        // Removing shifts indices — collect targets per list and remove from the end so earlier indices stay valid.
        var dropCards = new List<int>();
        var dropRelics = new List<int>();
        int droppedCount = 0;
        foreach ((int index, int weight, _) in order)
        {
            if (used <= budget.Capacity)
            {
                break;
            }

            used -= weight;
            if (index < relicBase)
            {
                dropCards.Add(index);
            }
            else
            {
                dropRelics.Add(index - relicBase);
            }

            droppedCount++;
        }

        dropCards.Sort(static (a, b) => b.CompareTo(a));
        foreach (int i in dropCards)
        {
            config.Cards.RemoveAt(i);
        }

        dropRelics.Sort(static (a, b) => b.CompareTo(a));
        foreach (int i in dropRelics)
        {
            config.Relics.RemoveAt(i);
        }

        return droppedCount;
    }

    /// <summary>
    /// Removes copies from <paramref name="items"/> until its count is ≤ <paramref name="max"/>, always removing the
    /// lowest-durability copy first. Returns the number removed. 把副本数降到 ≤ max，每次移除最低耐久的一份；返回移除数。
    /// </summary>
    private static int DropBelowCount<T>(List<T> items, int max, Func<T, int> durabilityOf)
    {
        int dropped = 0;
        while (items.Count > Math.Max(0, max))
        {
            int worst = 0;
            for (int i = 1; i < items.Count; i++)
            {
                if (durabilityOf(items[i]) < durabilityOf(items[worst]))
                {
                    worst = i;
                }
            }

            items.RemoveAt(worst);
            dropped++;
        }

        return dropped;
    }
}

/// <summary>
/// The current carry limit: either the legacy per-kind count caps (OFF) or the unified capacity pool (ON). Provides a
/// single "how many more of this item fit" query so the hub, the gear-code import, and the carry clamp all enforce the
/// same rule — OFF: cards ≤ MaxCarryCards, relics ≤ MaxCarryRelics; ON: cards + relics ≤ CarryCapacity by rarity weight.
/// 当前携带限制：OFF 为旧的每类数量上限，ON 为统一容量池。提供统一的「还能带几份该物品」查询，让仓库、战备码导入与携带钳制
/// 执行同一条规则——OFF：卡 ≤ MaxCarryCards、遗物 ≤ MaxCarryRelics；ON：卡 + 遗物 ≤ CarryCapacity（按稀有度权重）。
/// </summary>
public readonly struct CarryBudget
{
    private readonly int _maxCards;
    private readonly int _maxRelics;
    private readonly int _capacity;
    private readonly bool _usesCapacity;

    private CarryBudget(int capacity)
    {
        _usesCapacity = true;
        _capacity = capacity;
        _maxCards = 0;
        _maxRelics = 0;
    }

    private CarryBudget(int maxCards, int maxRelics)
    {
        _usesCapacity = false;
        _capacity = 0;
        _maxCards = maxCards;
        _maxRelics = maxRelics;
    }

    /// <summary>The active limit from settings: ON → capacity pool, OFF → count caps. 从设置读取当前限制。</summary>
    public static CarryBudget FromSettings()
    {
        ExtractionSettings s = ExtractionSettingsPage.Current;
        return s.CarryCapacityEnabled
            ? new CarryBudget(Math.Max(0, s.CarryCapacity))
            : new CarryBudget(Math.Max(0, s.MaxCarryCards), Math.Max(0, s.MaxCarryRelics));
    }

    public bool UsesCapacity { get => _usesCapacity; }

    /// <summary>Total capacity in ON mode (0 when OFF). ON 模式总容量（OFF 时为 0）。</summary>
    public int Capacity => _capacity;

    /// <summary>Card count cap in OFF mode (0 when ON). OFF 模式卡牌数量上限（ON 时为 0）。</summary>
    public int MaxCards => _maxCards;

    /// <summary>Relic count cap in OFF mode (0 when ON). OFF 模式遗物数量上限（ON 时为 0）。</summary>
    public int MaxRelics => _maxRelics;

    /// <summary>Remaining capacity under the given carry in ON mode. ON 模式下携带剩余容量。</summary>
    public int RemainingCapacity(CarryConfig config) => Math.Max(0, _capacity - CarryCapacity.UsedCapacity(config));

    /// <summary>
    /// How many more copies of <paramref name="id"/> fit on top of <paramref name="config"/>. OFF: remaining count cap.
    /// ON: floor of remaining capacity ÷ the item's weight — a partial slot can't be filled. 在给定携带之上还能再带几份该物品。
    /// OFF：剩余数量上限；ON：⌊剩余容量 ÷ 权重⌋（不满一格放不下）。
    /// </summary>
    public int MoreAllowed(CarryConfig config, CarryCodec.ItemKind kind, ModelId id)
    {
        if (!_usesCapacity)
        {
            return kind switch
            {
                CarryCodec.ItemKind.Card => Math.Max(0, _maxCards - config.Cards.Count),
                CarryCodec.ItemKind.Relic => Math.Max(0, _maxRelics - config.Relics.Count),
                _ => 0,
            };
        }

        int weight = kind switch
        {
            CarryCodec.ItemKind.Card => CarryCapacity.WeightForCard(id),
            CarryCodec.ItemKind.Relic => CarryCapacity.WeightForRelic(),
            _ => 0,
        };
        return weight <= 0 ? 0 : RemainingCapacity(config) / weight;
    }
}
