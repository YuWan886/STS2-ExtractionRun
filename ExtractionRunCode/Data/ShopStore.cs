using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;
using ExtractionRun.Settings;

namespace ExtractionRun.Data;

/// <summary>
/// Persistent shop state and pricing for the 搜打撤 hub shop (ModDataStore, SaveScope.Profile). The stock is rolled
/// once per real calendar day (first open) and prices are frozen for that day; a manual refresh re-rolls everything
/// at a fee (50 → +50 → cap 250) that resets on the day rollover. Buy price = the rolled vanilla price × the
/// settings multiplier (round); sell price = the DETERMINISTIC vanilla base price (no variance) × the settings ratio
/// × a durability factor (durability / rarity max, floored, min 1 gold; potions and no-durability mode factor 1).
/// 搜打撤商店的持久化状态与定价（ModDataStore, SaveScope.Profile）。库存每个现实日历日（首次打开）roll 一次并冻结当天价格；
/// 手动刷新全量重 roll，费用 50 → +50 → 封顶 250，随翻页重置。买价 = roll 出的原版价 × 设置倍率（四舍五入）；
/// 卖价 = 确定性原版基准价（不带浮动）× 设置比例 × 耐久系数（耐久/稀有度上限，向下取整，最低 1 金；药水与无耐久模式系数为 1）。
/// </summary>
public static class ShopStore
{
    public const string DataKey = "shop";

    /// <summary>Kind keys stored in <see cref="ShopEntry.Kind"/>. 商店条目中存储的物品种类键。</summary>
    public const string KindCard = "card";
    public const string KindRelic = "relic";
    public const string KindPotion = "potion";

    /// <summary>Shop slot counts (doubled — the buy tab shows 卡牌 / 遗物 / 药水 sections with a fuller stock).
    /// 商店槽位数（已翻倍——购买页按卡牌/遗物/药水分区展示，库存更足）。</summary>
    private const int ColoredCardSlots = 12;
    private const int ColorlessCardSlots = 4;
    private const int RelicSlots = 8;
    private const int PotionSlots = 6;

    /// <summary>Stock layout version — bump when slot counts change so a same-day old stock re-rolls once.
    /// 库存布局版本——槽位数变化时递增，让当天旧库存重 roll 一次。</summary>
    private const int StockLayoutVersion = 1;

    /// <summary>Manual refresh fee curve (hard-coded): first refresh 50, +50 each, capped at 250; resets daily.
    /// 手动刷新费用曲线（硬编码）：首刷 50，每次 +50，封顶 250；随翻页重置。</summary>
    public const int RefreshBaseCost = 50;
    public const int RefreshStep = 50;
    public const int RefreshMaxCost = 250;

    /// <summary>Registers the shop slot. Must run inside <c>BeginModDataRegistration</c>. 注册商店槽位。</summary>
    public static void Register()
    {
        ModDataStore.For(Entry.ModId).Register(
            key: DataKey,
            fileName: "shop.json",
            scope: SaveScope.Profile,
            defaultFactory: () => new ShopData(),
            autoCreateIfMissing: true);
    }

    /// <summary>The live shop for the current profile. 当前存档的商店实例。</summary>
    public static ShopData Current => RitsuLibFramework.GetDataStore(Entry.ModId).Get<ShopData>(DataKey);

    /// <summary>Local calendar date the day rollover keys on. 翻页所依据的本地日历日期。</summary>
    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// Rolls a fresh stock if this is the first open of the day (or the state is empty/corrupt) and resets the refresh
    /// counter — the daily auto-refresh. Idempotent within a day: same-day opens keep the frozen stock. Called on shop open.
    /// 若为当天首次打开（或状态为空/损坏）则全量重 roll 库存并重置刷新计数——每日自动刷新。同一天内幂等：当日重复打开保持冻结库存。
    /// </summary>
    public static void EnsureStocked()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<ShopData>(DataKey, data =>
        {
            // Re-roll when the stock layout version changed (slot counts) so a same-day old stock picks up new counts.
            if (data.Version >= StockLayoutVersion && data.StockDate == Today() && data.Entries.Count > 0)
            {
                return;
            }

            data.Version = StockLayoutVersion;
            data.StockDate = Today();
            data.RefreshCount = 0;
            RollStock(data);
        });
        store.Save(DataKey);
    }

    /// <summary>Manual refresh fee for the current counter. 当前刷新费用。</summary>
    public static int RefreshCost(ShopData shop) =>
        Math.Min(RefreshBaseCost + RefreshStep * shop.RefreshCount, RefreshMaxCost);

    /// <summary>
    /// Charges the refresh fee and re-rolls the whole stock (every slot, including unsold ones). Returns false when the
    /// warehouse cannot afford it. Deducts from the warehouse balance and bumps the refresh counter; both persist.
    /// 扣除刷新费并全量重 roll 库存（含未售出的槽位）。金币不足返回 false。从仓库余额扣除并自增刷新计数，两者都落盘。
    /// </summary>
    public static bool TryManualRefresh(WarehouseData warehouse)
    {
        var shop = Current;
        int cost = RefreshCost(shop);
        if (warehouse.Gold < cost)
        {
            return false;
        }

        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<ShopData>(DataKey, data =>
        {
            data.RefreshCount++;
            RollStock(data);
        });
        store.Save(DataKey);
        WarehouseStore.RemoveGold(cost);
        return true;
    }

    /// <summary>
    /// Attempts a purchase: resolves + builds the bought item FIRST (a corrupt/unresolvable entry costs nothing), then
    /// checks the warehouse balance, deposits the item (normalized, full durability) and deducts the gold, then marks
    /// the slot sold. Returns false on any failure with no state changed. 尝试购买：先解析并构建物品（损坏/解析不到的条目不扣任何
    /// 东西），再校验余额、入库（归一化满耐久）、扣款，最后标记已售。任何失败都返回 false 且不改动状态。
    /// </summary>
    public static bool TryBuy(ShopEntry entry, WarehouseData warehouse)
    {
        ModelId id;
        try
        {
            id = ModelId.Deserialize(entry.Id);
        }
        catch (Exception)
        {
            return false;
        }

        WarehouseCard? card = null;
        WarehouseRelic? relic = null;
        SerializablePotion? potion = null;
        try
        {
            if (entry.Kind == KindCard)
            {
                CardModel? model = ModelDb.GetByIdOrNull<CardModel>(id);
                if (model == null)
                {
                    return false;
                }

                card = new WarehouseCard
                {
                    Card = WarehouseStore.NormalizeCard(model.ToMutable().ToSerializable()),
                    Durability = WarehouseStore.MaxDurabilityForCard(id),
                };
            }
            else if (entry.Kind == KindRelic)
            {
                RelicModel? model = ModelDb.GetByIdOrNull<RelicModel>(id);
                if (model == null)
                {
                    return false;
                }

                relic = new WarehouseRelic
                {
                    Relic = WarehouseStore.NormalizeRelic(model.ToMutable().ToSerializable()),
                    Durability = WarehouseStore.MaxDurabilityForRelic(),
                };
            }
            else
            {
                PotionModel? model = ModelDb.GetByIdOrNull<PotionModel>(id);
                if (model == null)
                {
                    return false;
                }

                potion = WarehouseStore.NormalizePotion(model.ToMutable().ToSerializable(0));
            }
        }
        catch (Exception)
        {
            return false;
        }

        int price = BuyPrice(entry);
        if (warehouse.Gold < price)
        {
            return false;
        }

        if (card != null)
        {
            WarehouseStore.Deposit(new[] { card }, null, null, 0);
        }
        else if (relic != null)
        {
            WarehouseStore.Deposit(null, new[] { relic }, null, 0);
        }
        else
        {
            WarehouseStore.Deposit(null, null, new[] { potion! }, 0);
        }

        WarehouseStore.RemoveGold(price);
        entry.Sold = true;
        RitsuLibFramework.GetDataStore(Entry.ModId).Save(DataKey);
        return true;
    }

    /// <summary>Buy price for a stock entry: the rolled price × the settings buy multiplier, rounded.
    /// 买入价：roll 价 × 设置买入倍率，四舍五入。</summary>
    public static int BuyPrice(ShopEntry entry) =>
        (int)Math.Round(entry.Price * Math.Clamp(ExtractionSettingsPage.Current.ShopPriceMultiplier, 0.1, 10.0));

    /// <summary>
    /// Sell value for ONE copy: the deterministic vanilla base price × the settings sell ratio × a durability factor
    /// (durability / rarity max), floored, minimum 1 gold. Potions and no-durability mode factor 1 (they never
    /// decrement). 单份售价：确定性原版基准价 × 设置卖出比例 × 耐久系数（耐久/稀有度上限），向下取整，最低 1 金。
    /// 药水与无耐久模式系数为 1（它们永不递减）。
    /// </summary>
    public static int SellValue(string kind, ModelId id, int durability)
    {
        double ratio = Math.Clamp(ExtractionSettingsPage.Current.ShopSellRatio, 0.0, 1.0);
        double factor = 1.0;
        if (kind != KindPotion && WarehouseStore.IsDurabilityEnabled)
        {
            factor = Math.Max(1, durability) / (double)Math.Max(1, MaxDurabilityFor(kind, id));
        }

        int value = (int)Math.Floor(BasePrice(kind, id) * ratio * factor);
        return Math.Max(1, value);
    }

    /// <summary>
    /// Deterministic vanilla base price for any id — the SELL price anchor (no per-item variance). Cards by rarity
    /// (150/75/50, colorless ×1.15); potions by rarity (100/75/50); relics by rarity, mirroring the vanilla
    /// <c>MerchantCost</c> table for shop rarities with sensible bounded fallbacks for Starter/Event/Ancient (which the
    /// base game marks unsellable at 999999999 — that sentinel would make selling them absurd).
    /// 任意 id 的确定性原版基准价——卖价基准（不带浮动）。卡牌按稀有度（150/75/50，无色 ×1.15）；药水按稀有度（100/75/50）；
    /// 遗物按稀有度，商店稀有度对齐原版 MerchantCost，Starter/Event/Ancient 给出合理有界的兜底（原版用 999999999 哨兵标记不可售，
    /// 直接沿用会导致出售天价）。
    /// </summary>
    public static int BasePrice(string kind, ModelId id)
    {
        try
        {
            return kind switch
            {
                KindCard => BaseCardPrice(ModelDb.GetByIdOrNull<CardModel>(id)),
                KindRelic => BaseRelicPrice(ModelDb.GetByIdOrNull<RelicModel>(id)),
                _ => BasePotionPrice(ModelDb.GetByIdOrNull<PotionModel>(id)),
            };
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>Max durability a bought copy of a kind/id is granted (sell factor denominator). Potions never decrement
    /// and have no durability — the card lookup would hard-cast a potion id and throw, so potions return a never-used 1.
    /// 该种类/ id 副本的满耐久（售价系数分母）。药水不递减、无耐久——卡牌查找会对药水 id 硬转崩溃，故药水返回不会被用到的 1。</summary>
    public static int MaxDurabilityFor(string kind, ModelId id) =>
        kind switch
        {
            KindRelic => WarehouseStore.MaxDurabilityForRelic(),
            KindPotion => 1,
            _ => WarehouseStore.MaxDurabilityForCard(id),
        };

    /// <summary>Persists the shop state (used after any mutation). 持久化商店状态。</summary>
    public static void Persist()
    {
        RitsuLibFramework.GetDataStore(Entry.ModId).Save(DataKey);
    }

    // ----- Stock generation 库存生成 -----

    private static void RollStock(ShopData data)
    {
        var rng = new Random();
        data.Entries.Clear();
        AddCardEntries(data, rng);
        AddRelicEntries(data, rng);
        AddPotionEntries(data, rng);
    }

    private static void AddCardEntries(ShopData data, Random rng)
    {
        // No character is selected at the hub, so "colored" = every non-colorless pool (the vanilla merchant splits
        // 2A/2S/1P + 2 colorless for the current character; we just widen the colored count).
        var colored = ModelDb.AllCards
            .Where(c => c.Pool is not ColorlessCardPool && IsShopCardRarity(c.Rarity))
            .Distinct()
            .ToList();
        var colorless = ModelDb.AllCards
            .Where(c => c.Pool is ColorlessCardPool && c.Rarity is CardRarity.Uncommon or CardRarity.Rare)
            .Distinct()
            .ToList();

        for (int i = 0; i < ColoredCardSlots; i++)
        {
            AddCardEntry(data, rng, colored, RollCardRarity(rng));
        }

        for (int i = 0; i < ColorlessCardSlots; i++)
        {
            AddCardEntry(data, rng, colorless, rng.NextDouble() < 0.5 ? CardRarity.Uncommon : CardRarity.Rare);
        }
    }

    private static void AddCardEntry(ShopData data, Random rng, List<CardModel> pool, CardRarity rarity)
    {
        List<CardModel> candidates = pool.Where(c => c.Rarity == rarity).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        CardModel pick = candidates[rng.Next(candidates.Count)];
        pool.Remove(pick);
        AddEntry(data, KindCard, pick.Id, RollPrice(KindCard, pick.Id, rng));
    }

    private static void AddRelicEntries(ShopData data, Random rng)
    {
        // Shop rarities only — Ancient/Starter/Event have no real MerchantCost. Respect IsAllowedInShops like vanilla.
        var pool = ModelDb.AllRelics
            .Where(r => r.IsAllowedInShops
                        && r.Rarity is RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop)
            .Distinct()
            .ToList();

        for (int i = 0; i < RelicSlots; i++)
        {
            List<RelicModel> candidates = pool.Where(r => r.Rarity == RollRelicRarity(rng)).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            RelicModel pick = candidates[rng.Next(candidates.Count)];
            pool.Remove(pick);
            AddEntry(data, KindRelic, pick.Id, RollPrice(KindRelic, pick.Id, rng));
        }
    }

    private static void AddPotionEntries(ShopData data, Random rng)
    {
        var pool = ModelDb.AllPotions
            .Where(p => p.Rarity is PotionRarity.Common or PotionRarity.Uncommon or PotionRarity.Rare)
            .Distinct()
            .ToList();

        for (int i = 0; i < PotionSlots; i++)
        {
            if (pool.Count == 0)
            {
                break;
            }

            PotionModel pick = pool[rng.Next(pool.Count)];
            pool.Remove(pick);
            AddEntry(data, KindPotion, pick.Id, RollPrice(KindPotion, pick.Id, rng));
        }
    }

    private static void AddEntry(ShopData data, string kind, ModelId id, int price)
    {
        data.Entries.Add(new ShopEntry { Kind = kind, Id = id.ToString(), Price = price });
    }

    /// <summary>Vanilla shop card rarity weights (Common 45% / Uncommon 35% / Rare 20% — the base game rolls its own
    /// odds via PlayerOdds; this is a hub-side approximation since no run exists). 商店卡牌稀有度权重。</summary>
    private static CardRarity RollCardRarity(Random rng) => rng.NextDouble() switch
    {
        < 0.45 => CardRarity.Common,
        < 0.80 => CardRarity.Uncommon,
        _ => CardRarity.Rare,
    };

    /// <summary>Shop relic rarity weights (Common 30% / Uncommon 30% / Rare 20% / Shop 20%).
    /// 商店遗物稀有度权重。</summary>
    private static RelicRarity RollRelicRarity(Random rng) => rng.NextDouble() switch
    {
        < 0.30 => RelicRarity.Common,
        < 0.60 => RelicRarity.Uncommon,
        < 0.80 => RelicRarity.Rare,
        _ => RelicRarity.Shop,
    };

    private static bool IsShopCardRarity(CardRarity rarity) =>
        rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;

    /// <summary>Rolled price with the vanilla per-item variance: cards/potions ±5%, relics ±15%.
    /// 带原版逐件浮动的 roll 价：卡/药水 ±5%，遗物 ±15%。</summary>
    private static int RollPrice(string kind, ModelId id, Random rng)
    {
        int basePrice = BasePrice(kind, id);
        double factor = kind == KindRelic ? 0.85 + rng.NextDouble() * 0.30 : 0.95 + rng.NextDouble() * 0.10;
        return (int)Math.Round(basePrice * factor);
    }

    private static int BaseCardPrice(CardModel? card)
    {
        if (card == null)
        {
            return 50;
        }

        int price = card.Rarity switch
        {
            CardRarity.Rare => 150,
            CardRarity.Uncommon => 75,
            _ => 50,
        };
        if (card.Pool is ColorlessCardPool)
        {
            price = (int)Math.Round(price * 1.15f);
        }

        return price;
    }

    private static int BasePotionPrice(PotionModel? potion) => potion?.Rarity switch
    {
        PotionRarity.Rare => 100,
        PotionRarity.Uncommon => 75,
        _ => 50,
    };

    private static int BaseRelicPrice(RelicModel? relic) => relic?.Rarity switch
    {
        RelicRarity.Common => 175,
        RelicRarity.Uncommon => 225,
        RelicRarity.Rare => 275,
        RelicRarity.Shop => 200,
        RelicRarity.Starter => 150,
        RelicRarity.Event => 250,
        RelicRarity.Ancient => 500,
        _ => 50,
    };
}
