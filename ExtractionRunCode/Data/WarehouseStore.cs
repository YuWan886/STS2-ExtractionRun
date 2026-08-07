using System.Collections.Generic;
using System.Linq;
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
            data.Gold = ClampGold(data.Gold + 1000);

            foreach (CardModel card in ModelDb.AllCards
                         .Where(c => c.Rarity is CardRarity.Basic or CardRarity.Common)
                         .GroupBy(c => c.Id)
                         .Select(g => g.First()))
            {
                data.Cards.Add(card.ToMutable().ToSerializable());
            }

            foreach (RelicModel relic in ModelDb.AllRelics
                         .Where(r => r.Rarity is RelicRarity.Starter or RelicRarity.Common)
                         .GroupBy(r => r.Id)
                         .Select(g => g.First()))
            {
                data.Relics.Add(relic.ToMutable().ToSerializable());
            }
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Deposits extraction loot into the warehouse. Appends (a deck clone never reaches here — see DepositFilter).
    /// 把撤离战利品追加存入仓库。
    /// </summary>
    public static void Deposit(IEnumerable<SerializableCard>? cards, IEnumerable<SerializableRelic>? relics,
        IEnumerable<SerializablePotion>? potions, int gold)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            if (cards != null)
            {
                data.Cards.AddRange(cards);
            }

            if (relics != null)
            {
                data.Relics.AddRange(relics);
            }

            if (potions != null)
            {
                data.Potions.AddRange(potions);
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
            foreach (SerializableCard carriedCard in carried.Cards)
            {
                int index = data.Cards.FindIndex(c => SerializableCardEquals(c, carriedCard));
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

    private static bool SerializableCardEquals(SerializableCard a, SerializableCard b)
    {
        return a.Id == b.Id
               && a.CurrentUpgradeLevel == b.CurrentUpgradeLevel
               && Equals(a.Enchantment?.Id, b.Enchantment?.Id)
               && a.Enchantment?.Amount == b.Enchantment?.Amount;
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
