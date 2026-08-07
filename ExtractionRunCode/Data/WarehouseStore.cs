using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;

namespace ExtractionRun.Data;

/// <summary>
/// Read/write access to the persistent warehouse (ModDataStore, SaveScope.Profile). Handles registration, the
/// first-time seed, depositing extraction loot and consuming carried items at run start.
/// 仓库的持久化读写（ModDataStore, SaveScope.Profile）：注册、首次种子、存入战利品、开跑时消耗携带物。
/// </summary>
public static class WarehouseStore
{
    public const string DataKey = "warehouse";

    /// <summary>Gold is clamped to avoid int overflow and absurd UI. 金币上限，防止溢出。</summary>
    public const int MaxGold = 9_999_999;

    /// <summary>Registers the warehouse data slot. Must run inside <c>BeginModDataRegistration</c>. 注册仓库数据槽位。</summary>
    public static void Register()
    {
        ModDataStore.For(Entry.ModId).Register(
            key: DataKey,
            fileName: "warehouse.json",
            scope: SaveScope.Profile,
            defaultFactory: () => new WarehouseData(),
            autoCreateIfMissing: true);
    }

    /// <summary>The live warehouse for the current profile. 当前存档的仓库。</summary>
    public static WarehouseData Current => RitsuLibFramework.GetDataStore(Entry.ModId).Get<WarehouseData>(DataKey);

    /// <summary>
    /// Seeds the warehouse on first use: all Basic+Common cards, all Starter+Common relics and 1000 gold.
    /// Idempotent — guarded by <see cref="WarehouseData.Seeded"/>. 首次使用发放种子：全部初始+普通卡牌、初始+普通遗物、1000金币。
    /// </summary>
    public static void EnsureSeeded()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            if (data.Seeded)
            {
                return;
            }

            data.Seeded = true;
            data.Version++;
            data.Gold = ClampGold(data.Gold + 1000);

            foreach (CardModel card in ModelDb.AllCards
                         .Where(c => c.Rarity is CardRarity.Basic or CardRarity.Common)
                         .GroupBy(c => c.Id)
                         .Select(g => g.First()))
            {
                data.Cards.Add(NormalizeCard(card.ToMutable().ToSerializable()));
            }

            foreach (RelicModel relic in ModelDb.AllRelics
                         .Where(r => r.Rarity is RelicRarity.Starter or RelicRarity.Common)
                         .GroupBy(r => r.Id)
                         .Select(g => g.First()))
            {
                data.Relics.Add(NormalizeRelic(relic.ToMutable().ToSerializable()));
            }
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// One-shot legacy migration: warehouses written before the base-only change may hold upgraded / enchanted /
    /// prop-carrying cards and relics. Normalize every entry to its base state on first open after the update, so the
    /// hub's id-based grouping, the carry preview and the consume matching all line up. Idempotent — guarded by
    /// <see cref="WarehouseData.Normalized"/>. 一次性旧档迁移：基础化改动前的仓库可能存有升级/附魔/带属性的卡与遗物，
    /// 更新后首次打开原地归一，保证按 id 分组、携带预览与消耗匹配一致。
    /// </summary>
    public static void EnsureNormalized()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            if (data.Normalized)
            {
                return;
            }

            data.Normalized = true;
            data.Version++;

            for (int i = 0; i < data.Cards.Count; i++)
            {
                data.Cards[i] = NormalizeCard(data.Cards[i]);
            }

            for (int i = 0; i < data.Relics.Count; i++)
            {
                data.Relics[i] = NormalizeRelic(data.Relics[i]);
            }

            for (int i = 0; i < data.Potions.Count; i++)
            {
                data.Potions[i] = NormalizePotion(data.Potions[i]);
            }
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Persists the live warehouse state (used for the hub's in-memory filter/search state before close).
    /// 持久化当前仓库（用于关闭仓库前把界面过滤/搜索状态落盘）。
    /// </summary>
    public static void Persist()
    {
        RitsuLibFramework.GetDataStore(Entry.ModId).Save(DataKey);
    }

    /// <summary>
    /// Deposits extraction loot into the warehouse. Every item is normalized to its BASE state first (upgrades,
    /// enchantments, props, potion slot indices are stripped) — the warehouse only ever holds plain cards. Appends
    /// (a deck clone never reaches here — see DepositFilter). 把撤离战利品追加存入仓库，进库前统一归一到基础态。
    /// </summary>
    public static void Deposit(IEnumerable<SerializableCard>? cards, IEnumerable<SerializableRelic>? relics,
        IEnumerable<SerializablePotion>? potions, int gold)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            data.Version++;

            if (cards != null)
            {
                data.Cards.AddRange(cards.Select(NormalizeCard));
            }

            if (relics != null)
            {
                data.Relics.AddRange(relics.Select(NormalizeRelic));
            }

            if (potions != null)
            {
                data.Potions.AddRange(potions.Select(NormalizePotion));
            }

            data.Gold = ClampGold(data.Gold + gold);
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Removes the carried items from this machine's warehouse (Tarkov-style: they are consumed on entry).
    /// Only called for the LOCAL player on each machine. 从本机仓库移除已携带进局的物品（进局即消耗）。
    /// </summary>
    public static void ConsumeCarried(CarryConfig carried)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            data.Version++;

            foreach (SerializableCard carriedCard in carried.Cards)
            {
                int index = data.Cards.FindIndex(c => c.Id == carriedCard.Id);
                if (index >= 0)
                {
                    data.Cards.RemoveAt(index);
                }
            }

            foreach (SerializableRelic carriedRelic in carried.Relics)
            {
                int index = data.Relics.FindIndex(r => r.Id == carriedRelic.Id);
                if (index >= 0)
                {
                    data.Relics.RemoveAt(index);
                }
            }

            foreach (SerializablePotion carriedPotion in carried.Potions)
            {
                int index = data.Potions.FindIndex(p => p.Id == carriedPotion.Id);
                if (index >= 0)
                {
                    data.Potions.RemoveAt(index);
                }
            }

            data.Gold = ClampGold(data.Gold - carried.Gold);
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Strips a card down to its base state: no upgrade, no enchantment, no saved props, no deck-floor marker.
    /// Mutates and returns the same instance (callers hold throwaway serializables). 把卡牌归一为基础态（去升级/附魔/属性）。
    /// </summary>
    public static SerializableCard NormalizeCard(SerializableCard card)
    {
        card.CurrentUpgradeLevel = 0;
        card.Enchantment = null;
        card.Props = null;
        card.FloorAddedToDeck = null;
        return card;
    }

    /// <summary>
    /// Strips a relic down to its base state: no saved props (stack amounts), no deck-floor marker. 把遗物归一为基础态（去属性）。
    /// </summary>
    public static SerializableRelic NormalizeRelic(SerializableRelic relic)
    {
        relic.Props = null;
        relic.FloorAddedToDeck = null;
        return relic;
    }

    /// <summary>
    /// Strips a potion's in-run slot index (meaningless in a stash). 清掉药水的局内栏位号（仓库里无意义）。
    /// </summary>
    public static SerializablePotion NormalizePotion(SerializablePotion potion)
    {
        potion.SlotIndex = 0;
        return potion;
    }

    private static int ClampGold(int gold)
    {
        if (gold < 0)
        {
            return 0;
        }

        return gold > MaxGold ? MaxGold : gold;
    }
}
