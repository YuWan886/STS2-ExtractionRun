using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Data;

/// <summary>
/// Resolves a decoded gear code against the receiving player's warehouse: importable items are cloned from warehouse
/// stock (clamped to what they own and to the carry limits), everything else is reported so the dialog can explain why.
/// Gold is clamped to the warehouse balance. Import never grants items — the receiver must have earned them (Tarkov
/// economy), so the applied carry always consumes existing stock at run start and never opens a duping path.
/// Each item's kind (card / relic / potion) is resolved from ModelDb by trying the three categories — the code itself
/// carries no kind marker, so a hand-reordered code still imports correctly.
/// 把解析出的战备码对接收者仓库落地：可导入的物品从仓库存量克隆（按持有量与携带上限收敛），其余记为缺失供弹窗说明原因。
/// 金币收敛到仓库余额。导入绝不凭空发放物品——接收者必须已拥有（塔科夫经济），因此应用后的携带在开跑时总是消耗既有库存，
/// 不会打开白嫖路径。每个物品的类别（卡/遗物/药水）从 ModelDb 按三种类别试解析——码本身不含类别标记，手调顺序也能正确导入。
/// </summary>
public static class CarryCodeImport
{
    /// <summary>Potions are always capped at the three vanilla potion slots (no setting). 药水固定三格（无设置项）。</summary>
    public const int MaxPotions = 3;

    private static readonly string CardCategory = ModelId.SlugifyCategory<CardModel>();
    private static readonly string RelicCategory = ModelId.SlugifyCategory<RelicModel>();
    private static readonly string PotionCategory = ModelId.SlugifyCategory<PotionModel>();

    /// <summary>The import verdict: the applied (clamped) carry plus the reasons items were dropped. 导入结果：收敛后的携带与各丢弃原因。</summary>
    public sealed class Result
    {
        /// <summary>The clamped carry to apply. 应用后的携带。</summary>
        public CarryConfig Applied { get; } = new();

        /// <summary>Normalized mod-id stems of items blocked by a missing mod. 因缺少 mod 被拦截的物品的 mod 茎。</summary>
        public List<string> MissingModStems { get; } = new();

        /// <summary>Items whose owner is loaded (or unknown base content) but whose id resolves to no model — version
        /// drift or a bad code. 归属 mod 已加载（或基础内容）但 id 解析不到模型的物品。</summary>
        public List<CarryCodec.CodeItem> Unrecognized { get; } = new();

        /// <summary>Total requested items that did not make it into <see cref="Applied"/> (missing mods, unrecognized,
        /// and insufficient stock combined). 未能进入 Applied 的物品总数（缺 mod + 无法识别 + 库存不足）。</summary>
        public int MissingCount { get; set; }

        /// <summary>True when the requested gold exceeded the warehouse balance and was clamped. 请求金币超出仓库余额并被收敛。</summary>
        public bool GoldClamped { get; set; }
    }

    public static Result Apply(CarryCodec.DecodedCarry code, WarehouseData warehouse, int maxCards, int maxRelics)
    {
        var result = new Result();
        int cardsLeft = Math.Max(0, maxCards);
        int relicsLeft = Math.Max(0, maxRelics);
        int potionsLeft = MaxPotions;

        var importedById = new Dictionary<ModelId, int>();

        foreach (CarryCodec.CodeItem item in code.Items)
        {
            ImportItem(result, item, warehouse, importedById, ref cardsLeft, ref relicsLeft, ref potionsLeft);
        }

        int desiredGold = Math.Max(0, code.Gold);
        int gold = Math.Min(desiredGold, Math.Max(0, warehouse.Gold));
        result.Applied.Gold = gold;
        result.GoldClamped = gold < desiredGold;
        return result;
    }

    /// <summary>The ModelId category string for a kind. 某类别对应的 ModelId 分类段。</summary>
    public static string CategoryFor(CarryCodec.ItemKind kind) => kind switch
    {
        CarryCodec.ItemKind.Card => CardCategory,
        CarryCodec.ItemKind.Relic => RelicCategory,
        _ => PotionCategory,
    };

    private static void ImportItem(Result result, CarryCodec.CodeItem item, WarehouseData warehouse,
        Dictionary<ModelId, int> importedById, ref int cardsLeft, ref int relicsLeft, ref int potionsLeft)
    {
        if (item.OwnerStem != null && !CarryCodeOwner.IsModLoaded(item.OwnerStem))
        {
            if (!result.MissingModStems.Contains(item.OwnerStem))
            {
                result.MissingModStems.Add(item.OwnerStem);
            }

            result.MissingCount += item.Count;
            return;
        }

        if (!TryResolveKind(item.Entry, out CarryCodec.ItemKind kind, out ModelId id))
        {
            result.Unrecognized.Add(item);
            result.MissingCount += item.Count;
            return;
        }

        int stock = StockCount(warehouse, kind, id);
        if (stock <= 0)
        {
            result.MissingCount += item.Count;
            return;
        }

        int budget = kind switch
        {
            CarryCodec.ItemKind.Card => cardsLeft,
            CarryCodec.ItemKind.Relic => relicsLeft,
            _ => potionsLeft,
        };
        int alreadyImported = importedById.GetValueOrDefault(id);
        int remainingStock = Math.Max(0, stock - alreadyImported);
        int importable = Math.Min(item.Count, Math.Min(remainingStock, budget));
        result.MissingCount += item.Count - importable;

        switch (kind)
        {
            case CarryCodec.ItemKind.Card:
                cardsLeft -= importable;
                break;
            case CarryCodec.ItemKind.Relic:
                relicsLeft -= importable;
                break;
            default:
                potionsLeft -= importable;
                break;
        }

        if (importable > 0)
        {
            importedById[id] = alreadyImported + importable;
            CloneFromWarehouse(result.Applied, warehouse, kind, id, importable);
        }
    }

    /// <summary>Resolves an entry to one of the three kinds by probing ModelDb (cards first, then relics, then potions).
    /// Shared with the import dialog's preview so it renders the same kind the import will use. 按 ModelDb 试解析 entry 的类别
    /// （卡→遗物→药水），与导入弹窗预览共用，保证预览与导入类别一致。</summary>
    public static bool TryResolveKind(string entry, out CarryCodec.ItemKind kind, out ModelId id)
    {
        id = new ModelId(CardCategory, entry);
        if (ModelDb.GetByIdOrNull<CardModel>(id) != null)
        {
            kind = CarryCodec.ItemKind.Card;
            return true;
        }

        id = new ModelId(RelicCategory, entry);
        if (ModelDb.GetByIdOrNull<RelicModel>(id) != null)
        {
            kind = CarryCodec.ItemKind.Relic;
            return true;
        }

        id = new ModelId(PotionCategory, entry);
        if (ModelDb.GetByIdOrNull<PotionModel>(id) != null)
        {
            kind = CarryCodec.ItemKind.Potion;
            return true;
        }

        kind = default;
        return false;
    }

    private static int StockCount(WarehouseData warehouse, CarryCodec.ItemKind kind, ModelId id) => kind switch
    {
        CarryCodec.ItemKind.Card => warehouse.Cards.Count(c => c.Card.Id == id),
        CarryCodec.ItemKind.Relic => warehouse.Relics.Count(r => r.Relic.Id == id),
        _ => warehouse.Potions.Count(p => p.Id == id),
    };

    /// <summary>
    /// Clones the lowest-durability warehouse copies of <paramref name="id"/> into the applied carry (the same
    /// worst-gear-first rule as the hub and the console remove). 把仓库中 id 对应的最低耐久副本克隆进应用的携带（与大厅、
    /// 控制台删除一致的「先带最破」规则）。
    /// </summary>
    private static void CloneFromWarehouse(CarryConfig applied, WarehouseData warehouse, CarryCodec.ItemKind kind,
        ModelId id, int count)
    {
        int remaining = count;
        switch (kind)
        {
            case CarryCodec.ItemKind.Card:
                foreach (WarehouseCard wc in warehouse.Cards
                             .Where(c => c.Card.Id == id)
                             .OrderBy(c => c.Durability))
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    applied.Cards.Add(CloneCard(wc));
                    remaining--;
                }

                break;
            case CarryCodec.ItemKind.Relic:
                foreach (WarehouseRelic wr in warehouse.Relics
                             .Where(r => r.Relic.Id == id)
                             .OrderBy(r => r.Durability))
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    applied.Relics.Add(CloneRelic(wr));
                    remaining--;
                }

                break;
            default:
                foreach (SerializablePotion sp in warehouse.Potions)
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    if (sp.Id != id)
                    {
                        continue;
                    }

                    applied.Potions.Add(ClonePotion(sp));
                    remaining--;
                }

                break;
        }
    }

    /// <summary>Shallow-copies a stored item so the applied carry never aliases warehouse instances (which the hub
    /// treats as immutable). 浅拷贝物品，避免应用后的携带与仓库实例混用。</summary>
    private static WarehouseCard CloneCard(WarehouseCard c) => new()
    {
        Card = new SerializableCard
        {
            Id = c.Card.Id,
            CurrentUpgradeLevel = c.Card.CurrentUpgradeLevel,
            Enchantment = c.Card.Enchantment,
            Props = c.Card.Props,
            FloorAddedToDeck = c.Card.FloorAddedToDeck,
        },
        Durability = c.Durability,
    };

    private static WarehouseRelic CloneRelic(WarehouseRelic r) => new()
    {
        Relic = new SerializableRelic
        {
            Id = r.Relic.Id,
            Props = r.Relic.Props,
            FloorAddedToDeck = r.Relic.FloorAddedToDeck,
        },
        Durability = r.Durability,
    };

    private static SerializablePotion ClonePotion(SerializablePotion p) => new()
    {
        Id = p.Id,
        SlotIndex = p.SlotIndex,
    };
}
