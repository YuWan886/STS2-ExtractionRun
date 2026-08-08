using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
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
            GrantInitialItems(data);
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Wipes the warehouse and re-grants the initial seed (all Basic+Common cards, all Starter+Common relics, 1000 gold)
    /// — the console reset command. The idempotent migration flags (<see cref="WarehouseData.Seeded"/>/<see cref="WarehouseData.Normalized"/>
    /// /<see cref="WarehouseData.IdentityRepaired"/>) and the persisted hub filter/search state are deliberately left untouched:
    /// this is a content reset, not a re-migration.
    /// 清空仓库并重新发放初始种子（初始/普通卡牌、初始/普通遗物、1000金币）——控制台重置指令。迁移标志与界面过滤状态不动。
    /// 此乃内容重置，非重新迁移。
    /// </summary>
    public static void Reset()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            data.Version++;
            data.Cards.Clear();
            data.Relics.Clear();
            data.Potions.Clear();
            data.Gold = 0;
            GrantInitialItems(data);
        });
        store.Save(DataKey);
    }

    /// <summary>Grants the first-use seed into a warehouse (starter/common cards + relics + 1000 gold). 发放初始种子。</summary>
    private static void GrantInitialItems(WarehouseData data)
    {
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
    /// One-shot legacy migration for the identity fix: pre-fix <see cref="NormalizeCard"/> wiped <c>Props</c> on every
    /// card, which left identity cards (e.g. MadScience — its base has <c>Type = None</c>) unplayable in the warehouse.
    /// Re-run normalization over every stored card: growth is stripped again (already base), and identity cards are
    /// re-filled with a valid default. Idempotent — guarded by <see cref="WarehouseData.IdentityRepaired"/>.
    /// 身份修复的一次性迁移：旧版 NormalizeCard 清空了全部 Props，把身份牌（如疯狂科学，基础态 Type 为 None）抹成不可打。
    /// 对库存每张卡重跑归一化：成长牌再次剥回基础态，身份牌回填有效默认。幂等——由 IdentityRepaired 守卫。
    /// </summary>
    public static void EnsureIdentityRepaired()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            if (data.IdentityRepaired)
            {
                return;
            }

            data.IdentityRepaired = true;
            data.Version++;

            for (int i = 0; i < data.Cards.Count; i++)
            {
                data.Cards[i] = NormalizeCard(data.Cards[i]);
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
    /// enchantments, run-scoped growth and potion slot indices stripped; identity cards keep their saved props — see
    /// <see cref="NormalizeCard"/>). A single un-normalizable card (e.g. a corrupt saved-prop type) is skipped rather than
    /// aborting the whole deposit, so one bad loot item never swallows the settlement. Appends (a deck clone never reaches
    /// here — see DepositFilter). 把撤离战利品追加存入仓库，进库前统一归一（升级/附魔/成长/栏位剥离，身份卡保留 Props）。
    /// 单张无法归一的卡（如非法保存属性类型）跳过而非让整次存入失败，避免一张坏牌吞掉整局结算。
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
                foreach (SerializableCard sc in cards)
                {
                    try
                    {
                        data.Cards.Add(NormalizeCard(sc));
                    }
                    catch (Exception ex)
                    {
                        Entry.Logger.Warn($"WarehouseStore.Deposit: skipping un-normalizable card {sc.Id}: {ex.Message}");
                    }
                }
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

    /// <summary>Removes up to <paramref name="count"/> copies of the given card id from the warehouse. Returns the number actually removed.
    /// 从仓库移除最多 count 张指定卡牌，返回实际移除数。</summary>
    public static int RemoveCards(ModelId id, int count) => RemoveCopies(id, count, d => d.Cards, c => c.Id);

    /// <summary>Removes up to <paramref name="count"/> copies of the given relic id from the warehouse. Returns the number actually removed.
    /// 从仓库移除最多 count 个指定遗物，返回实际移除数。</summary>
    public static int RemoveRelics(ModelId id, int count) => RemoveCopies(id, count, d => d.Relics, r => r.Id);

    /// <summary>Removes up to <paramref name="count"/> copies of the given potion id from the warehouse. Returns the number actually removed.
    /// 从仓库移除最多 count 瓶指定药水，返回实际移除数。</summary>
    public static int RemovePotions(ModelId id, int count) => RemoveCopies(id, count, d => d.Potions, p => p.Id);

    /// <summary>Removes gold (never below zero). Returns the new warehouse balance. 移除金币（不会扣成负数），返回新余额。</summary>
    public static int RemoveGold(int amount)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        int balance = 0;
        store.Modify<WarehouseData>(DataKey, data =>
        {
            data.Version++;
            data.Gold = ClampGold(data.Gold - Math.Max(0, amount));
            balance = data.Gold;
        });
        store.Save(DataKey);
        return balance;
    }

    private static int RemoveCopies<T>(ModelId id, int count, Func<WarehouseData, List<T>> listSelector,
        Func<T, ModelId?> idSelector) where T : class
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        int removed = 0;
        store.Modify<WarehouseData>(DataKey, data =>
        {
            data.Version++;
            List<T> list = listSelector(data);
            for (int i = list.Count - 1; i >= 0 && removed < count; i--)
            {
                if (idSelector(list[i]) == id)
                {
                    list.RemoveAt(i);
                    removed++;
                }
            }
        });
        store.Save(DataKey);
        return removed;
    }

    /// <summary>
    /// Strips a card down to base: no upgrade, no enchantment, no run-scoped growth, no deck-floor marker — except for
    /// "identity" cards whose saved props ARE the card's identity. A MadScience's tinker type/rider live in <c>Props</c>;
    /// without them its base model has <c>Type = CardType.None</c>, which the game never creates legitimately and whose
    /// <c>OnPlay</c> throws. So for those cards the props are kept (and re-filled with a valid default when missing, e.g.
    /// a legacy-stripped or console-added copy), while every other card is reduced to its plain base form. Mutates and
    /// returns the same instance (callers hold throwaway serializables).
    /// 把卡牌归一为：无升级/附魔/局内成长/入牌组楼层——但"身份牌"除外：疯狂科学的敲钟类型/附效存在 Props 里，剥掉后其基础
    /// 模型 Type 为 None（游戏从不合法产生、打出即崩）。故身份牌保留 Props（缺失时回填有效默认，覆盖旧档抹平/控制台添加的
    /// 副本），其余卡一律回到纯基础态。
    /// </summary>
    public static SerializableCard NormalizeCard(SerializableCard card)
    {
        card.CurrentUpgradeLevel = 0;
        card.Enchantment = null;
        card.FloorAddedToDeck = null;

        CardModel? model = card.Id == null ? null : ModelDb.GetByIdOrNull<CardModel>(card.Id);
        if (model != null && IsIdentityCard(model))
        {
            EnsureIdentityDefault(card, model);
        }
        else
        {
            card.Props = null;
        }

        return card;
    }

    /// <summary>
    /// An "identity card" is one whose base model (props stripped) is degenerate — <c>Type = None</c> means the card is
    /// unplayable, so its identity must live in its saved props. All other cards are plain playable bases whose props are
    /// run-scoped growth (GeneticAlgorithm etc.) and get stripped.
    /// 身份牌判定：剥离 Props 后基础模型退化（Type 为 None）即不可打，身份必然存于保存属性；其余卡基础形态可打，其 Props
    /// 是局内成长（如遗传算法），应剥除。
    /// </summary>
    private static bool IsIdentityCard(CardModel model)
    {
        try
        {
            return model.Type == CardType.None;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures an identity card carries VALID identity props. Props that restore a playable card are kept as-is; a
    /// missing/empty <c>Props</c> (legacy-stripped) or one that still resolves to <c>Type = None</c> (console-added
    /// MadScience carries <c>TinkerTimeType = None</c>) is re-filled with the card's canonical playable default —
    /// MadScience → Attack with no rider. Cards with an unknown valid default are left untouched (the carry-in guard in
    /// <see cref="ExtractionModifier"/> drops the few that remain degenerate).
    /// 保证身份卡带有"有效"身份属性：能还原为可打状态的 Props 原样保留；缺失（旧档抹平）或还原后仍为 Type=None
    /// （控制台添加的疯狂科学带着 TinkerTimeType=None）时，回填该卡可打的有效默认——疯狂科学 → 攻击型无附效。
    /// 未知有效默认的卡不动（残余退化卡由携带侧守卫丢弃）。
    /// </summary>
    private static void EnsureIdentityDefault(SerializableCard card, CardModel model)
    {
        SavedProperties? props = card.Props;
        bool degenerate = props == null;
        if (!degenerate)
        {
            try
            {
                CardModel probe = model.ToMutable();
                props!.Fill(probe);
                degenerate = probe.Type == CardType.None;
            }
            catch (Exception)
            {
                degenerate = true;
            }
        }

        if (!degenerate)
        {
            return;
        }

        if (model is MadScience)
        {
            MadScience copy = (MadScience)model.ToMutable();
            copy.TinkerTimeType = CardType.Attack;
            card.Props = SavedProperties.From(copy);
        }
    }

    /// <summary>
    /// Reward for clearing an extraction run: the character's full starting deck (all copies) and starting relics are
    /// deposited into the warehouse, normalized like any other loot — granted on every clear. Returns the granted items
    /// so the settlement screen can fold them into the deposited loot.
    /// 通关奖励：通关搜打撤后，把该角色的整套初始牌组（含全部张数）与初始遗物按普通战利品归一化入账，每次通关都发放。
    /// 返回本次发放的物品，供结算界面并入存入战利品展示。
    /// </summary>
    public static (List<SerializableCard> Cards, List<SerializableRelic> Relics) GrantCharacterCompletionReward(CharacterModel character)
    {
        string entry = character.Id.Entry;
        var grantedCards = new List<SerializableCard>();
        var grantedRelics = new List<SerializableRelic>();
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(DataKey, data =>
        {
            data.Version++;

            int grantedCardCount = character.StartingDeck.Count();
            int grantedRelicCount = character.StartingRelics.Count;
            Entry.Logger.Info($"GrantCharacterCompletionReward: clear with {entry} — " +
                              $"granting {grantedCardCount} starter cards and {grantedRelicCount} starter relics.");

            foreach (CardModel card in character.StartingDeck)
            {
                try
                {
                    SerializableCard sc = NormalizeCard(card.ToMutable().ToSerializable());
                    data.Cards.Add(sc);
                    grantedCards.Add(sc);
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"GrantCharacterCompletionReward: skipping starter card {card.Id}: {ex.Message}");
                }
            }

            foreach (RelicModel relic in character.StartingRelics)
            {
                try
                {
                    SerializableRelic sr = NormalizeRelic(relic.ToMutable().ToSerializable());
                    data.Relics.Add(sr);
                    grantedRelics.Add(sr);
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"GrantCharacterCompletionReward: skipping starter relic {relic.Id}: {ex.Message}");
                }
            }
        });
        store.Save(DataKey);
        return (grantedCards, grantedRelics);
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
