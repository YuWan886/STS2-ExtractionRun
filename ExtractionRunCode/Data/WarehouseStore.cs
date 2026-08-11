using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;
using ExtractionRun.Settings;

namespace ExtractionRun.Data;

/// <summary>
/// Read/write access to the persistent warehouse (ModDataStore, SaveScope.Profile). Handles registration, the
/// first-time seed, depositing extraction loot and consuming carried items at run start.
/// 仓库的持久化读写（ModDataStore, SaveScope.Profile）：注册、首次种子、存入战利品、开跑时消耗携带物。
/// </summary>
public static class WarehouseStore
{
    public const string DataKey = "warehouse";

    /// <summary>The no-durability warehouse key: a derived, disposable copy used while the durability toggle is OFF.
    /// Never written while ON; discarded (re-copied) on the next OFF. 无耐久仓库键：耐久开关关闭时使用的派生副本，ON 期间不被
    /// 写入，下一次 OFF 时丢弃并重新复制。</summary>
    public const string NoDurabilityDataKey = "warehouse_nodur";

    /// <summary>Gold is clamped to avoid int overflow and absurd UI. 金币上限，防止溢出。</summary>
    public const int MaxGold = 9_999_999;

    /// <summary>Registers both warehouse slots (durability + no-durability). Must run inside
    /// <c>BeginModDataRegistration</c>. 注册两个仓库槽位（耐久 + 无耐久）。</summary>
    public static void Register()
    {
        ModDataStore.For(Entry.ModId).Register(
            key: DataKey,
            fileName: "warehouse.json",
            scope: SaveScope.Profile,
            defaultFactory: () => new WarehouseData(),
            autoCreateIfMissing: true);

        ModDataStore.For(Entry.ModId).Register(
            key: NoDurabilityDataKey,
            fileName: "warehouse_nodur.json",
            scope: SaveScope.Profile,
            defaultFactory: () => new WarehouseData(),
            autoCreateIfMissing: true);
    }

    /// <summary>True when the durability system is enabled; gates both decrement and display. 耐久系统是否启用（决定递减与显示）。</summary>
    public static bool IsDurabilityEnabled => ExtractionSettingsPage.Current.DurabilityEnabled;

    /// <summary>The active warehouse key: the durability file while ON, the disposable no-durability copy while OFF.
    /// 当前活动仓库键：ON 用耐久文件，OFF 用一次性无耐久副本。</summary>
    public static string ActiveKey => IsDurabilityEnabled ? DataKey : NoDurabilityDataKey;

    /// <summary>The live warehouse for the current profile and active durability mode. 当前存档、当前模式下的仓库。</summary>
    public static WarehouseData Current => RitsuLibFramework.GetDataStore(Entry.ModId).Get<WarehouseData>(ActiveKey);

    /// <summary>
    /// Creates the no-durability copy from the durability warehouse the first time it is needed while OFF (hub open).
    /// The copy strips durability (every copy set to its rarity's max — OFF mode never decrements, so the value is only
    /// a representation). Guarded by file existence: the eager toggle handler already re-copies on every ON→OFF, so this
    /// lazy path only covers a toggle that ran before the profile data was initialized. 在 OFF 模式下首次需要时（打开仓库）
    /// 从耐久仓库创建无耐久副本：副本剥离耐久（每份置为稀有度上限——OFF 不递减，值仅作表示）。以文件是否存在为守卫：切换
    /// 处理已在每次 ON→OFF 时急切重复制，此懒路径只覆盖「切换时档案尚未初始化」的情况。
    /// </summary>
    public static void EnsureNoDurabilityCopy()
    {
        if (IsDurabilityEnabled)
        {
            return;
        }

        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        if (store.HasExistingData(NoDurabilityDataKey))
        {
            return;
        }

        try
        {
            if (!store.HasExistingData(DataKey))
            {
                return;
            }

            CopyDurabilityToNoDurability();
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"WarehouseStore.EnsureNoDurabilityCopy: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a durability-mode switch: ON→OFF re-copies the (frozen) durability warehouse into the no-durability
    /// copy; OFF→ON needs no file work (the durability file was never touched while OFF — it IS the restored state) but
    /// the stale no-durability copy is re-copied on the next OFF. Both directions re-sync the pending carry to the now
    /// active warehouse (count clamp + durability re-map) so a carried copy never references a wrong-durability stock.
    /// Called by the settings toggle's confirm handler; also safe when no hub is open (the active key simply flips).
    /// 应用耐久模式切换：ON→OFF 把（冻结的）耐久仓库重新复制进无耐久副本；OFF→ON 无需动文件（耐久文件在 OFF 期间从未被写——
    /// 它本身就是还原后的状态），但过期副本会在下一次 OFF 时重新复制。两个方向都会把待发携带重新对齐到当前活动仓库
    /// （数量钳制 + 耐久重映射），避免携带副本引用错误耐久的库存。
    /// </summary>
    public static void SwitchDurabilityMode(bool nowEnabled)
    {
        try
        {
            if (!nowEnabled)
            {
                CopyDurabilityToNoDurability();
            }
            // OFF→ON needs no file work: the durability file was never touched while OFF, so it IS the restored state.
            // The stale no-durability copy (if any) is re-copied on the next OFF.
            // OFF→ON 无需动文件：耐久文件在 OFF 期间从未被写，它本身就是还原后的状态；过期副本在下一次 OFF 时重新复制。
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"WarehouseStore.SwitchDurabilityMode: {ex.Message}");
        }

        try
        {
            // Re-sync the pending carry to the now-active warehouse: count clamp (OFF-acquired items don't exist in
            // the durability file and are dropped) + durability re-map (full OFF-mode values become the real ones).
            // 把待发携带重新对齐到当前活动仓库：数量钳制（OFF 期间新得的物品不存在于耐久文件，被剔除）+ 耐久重映射
            // （OFF 模式的满耐久值换成真实值）。
            PendingCarryStore.RevalidateAgainst(Current);
            PendingCarryStore.RevalidateDurability(Current);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"WarehouseStore.SwitchDurabilityMode: revalidate failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Overwrites the no-durability copy from the current durability warehouse, stripping durability (each copy set to
    /// its rarity's max). The durability file itself is never touched — it stays frozen as the ON-mode source of truth.
    /// 用当前耐久仓库覆盖无耐久副本并剥离耐久（每份置为稀有度上限）。耐久文件本身绝不动——它保持冻结，是 ON 模式的唯一真源。
    /// </summary>
    private static void CopyDurabilityToNoDurability()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        WarehouseData source = store.Get<WarehouseData>(DataKey);
        store.Modify<WarehouseData>(NoDurabilityDataKey, data =>
        {
            data.Version++;
            data.Seeded = source.Seeded;
            data.Normalized = source.Normalized;
            data.IdentityRepaired = source.IdentityRepaired;
            data.DurabilityInitialized = true;
            data.Gold = source.Gold;
            data.Filters = source.Filters ?? new WarehouseFilterState();
            data.Cards = source.Cards.Select(c => new WarehouseCard
            {
                Card = c.Card,
                Durability = MaxDurabilityForCard(c.Card.Id),
            }).ToList();
            data.Relics = source.Relics.Select(r => new WarehouseRelic
            {
                Relic = r.Relic,
                Durability = MaxDurabilityForRelic(),
            }).ToList();
            data.Potions = source.Potions.ToList();
        });
        store.Save(NoDurabilityDataKey);
    }

    /// <summary>
    /// Seeds the warehouse on first use: all Basic+Common cards, all Starter+Common relics and 1000 gold.
    /// Idempotent — guarded by <see cref="WarehouseData.Seeded"/>. 首次使用发放种子：全部初始+普通卡牌、初始+普通遗物、1000金币。
    /// </summary>
    public static void EnsureSeeded()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            if (data.Seeded)
            {
                return;
            }

            data.Seeded = true;
            data.Version++;
            GrantInitialItems(data);
        });
        store.Save(ActiveKey);
    }

    /// <summary>
    /// Wipes the warehouse and re-grants the initial seed (all Basic+Common cards, all Starter+Common relics, 1000 gold)
    /// — the console reset command. The idempotent migration flags (<see cref="WarehouseData.Seeded"/>/<see cref="WarehouseData.Normalized"/>
    /// /<see cref="WarehouseData.IdentityRepaired"/>/<see cref="WarehouseData.DurabilityInitialized"/>) and the persisted hub filter/search
    /// state are deliberately left untouched: this is a content reset, not a re-migration.
    /// 清空仓库并重新发放初始种子（初始/普通卡牌、初始/普通遗物、1000金币）——控制台重置指令。迁移标志与界面过滤状态不动。
    /// 此乃内容重置，非重新迁移。
    /// </summary>
    public static void Reset()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            data.Version++;
            data.Cards.Clear();
            data.Relics.Clear();
            data.Potions.Clear();
            data.Gold = 0;
            GrantInitialItems(data);
        });
        store.Save(ActiveKey);
    }

    /// <summary>
    /// Grants the first-use seed into a warehouse (starter/common cards + relics + 1000 gold). 发放初始种子。
    /// </summary>
    private static void GrantInitialItems(WarehouseData data)
    {
        data.Gold = ClampGold(data.Gold + 1000);

        foreach (CardModel card in ModelDb.AllCards
                     .Where(c => c.Rarity is CardRarity.Basic or CardRarity.Common)
                     .GroupBy(c => c.Id)
                     .Select(g => g.First()))
        {
            data.Cards.Add(new WarehouseCard
            {
                Card = NormalizeCard(card.ToMutable().ToSerializable()),
                Durability = MaxDurabilityForCard(card.Id),
            });
        }

        var excludedStems = new HashSet<string>(StringComparer.Ordinal);
        foreach (RelicModel relic in ModelDb.AllRelics
                     .Where(r => r.Rarity is RelicRarity.Starter or RelicRarity.Common)
                     .GroupBy(r => r.Id)
                     .Select(g => g.First()))
        {
            string? stem = CarryCodeOwner.ResolveOwnerStem(CarryCodec.ItemKind.Relic, relic.Id);
            if (stem != null && ExcludedSeedRelicStems.Contains(stem))
            {
                excludedStems.Add(stem);
                continue;
            }

            data.Relics.Add(new WarehouseRelic
            {
                Relic = NormalizeRelic(relic.ToMutable().ToSerializable()),
                Durability = MaxDurabilityForRelic(),
            });
        }

        if (excludedStems.Count > 0)
        {
            Entry.Logger.Info($"GrantInitialItems: excluded {excludedStems.Count} mod relic stem(s) from the seed " +
                              $"(e.g. 海克斯符文): {string.Join(", ", excludedStems)}.");
        }
    }

    /// <summary>Owner-mod stems whose relics the first-use seed must not grant.
    /// 首次种子不发放的归属 mod stem。</summary>
    private static readonly IReadOnlySet<string> ExcludedSeedRelicStems = new HashSet<string>(StringComparer.Ordinal)
    {
        "HEXTECH_RUNES",
        "HEXTECHRUNES",
    };

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
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            if (data.Normalized)
            {
                return;
            }

            data.Normalized = true;
            data.Version++;

            for (int i = 0; i < data.Cards.Count; i++)
            {
                data.Cards[i].Card = NormalizeCard(data.Cards[i].Card);
            }

            for (int i = 0; i < data.Relics.Count; i++)
            {
                data.Relics[i].Relic = NormalizeRelic(data.Relics[i].Relic);
            }

            for (int i = 0; i < data.Potions.Count; i++)
            {
                data.Potions[i] = NormalizePotion(data.Potions[i]);
            }
        });
        store.Save(ActiveKey);
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
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            if (data.IdentityRepaired)
            {
                return;
            }

            data.IdentityRepaired = true;
            data.Version++;

            for (int i = 0; i < data.Cards.Count; i++)
            {
                data.Cards[i].Card = NormalizeCard(data.Cards[i].Card);
            }
        });
        store.Save(ActiveKey);
    }

    /// <summary>
    /// One-shot legacy migration for the durability update: pre-durability saves deserialize with the 0 sentinel
    /// (the JsonConverter's legacy-shape branch). Backfill every ≤0 copy to its rarity's max so the warehouse only ever
    /// holds positive durability. Idempotent — guarded by <see cref="WarehouseData.DurabilityInitialized"/>.
    /// 耐久更新的一次性迁移：无耐久旧档以 0 哨兵反序列化（转换器旧版形状分支）。把每份 ≤0 回填为稀有度上限，保证仓库只存正耐久。
    /// 幂等——由 DurabilityInitialized 守卫。
    /// </summary>
    public static void EnsureDurabilityInitialized()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            if (data.DurabilityInitialized)
            {
                return;
            }

            data.DurabilityInitialized = true;
            data.Version++;

            foreach (WarehouseCard wc in data.Cards)
            {
                if (wc.Durability <= 0)
                {
                    wc.Durability = MaxDurabilityForCard(wc.Card.Id);
                }
            }

            foreach (WarehouseRelic wr in data.Relics)
            {
                if (wr.Durability <= 0)
                {
                    wr.Durability = MaxDurabilityForRelic();
                }
            }
        });
        store.Save(ActiveKey);
    }

    /// <summary>
    /// Persists the live warehouse state (used for the hub's in-memory filter/search state before close).
    /// 持久化当前仓库（用于关闭仓库前把界面过滤/搜索状态落盘）。
    /// </summary>
    public static void Persist()
    {
        RitsuLibFramework.GetDataStore(Entry.ModId).Save(ActiveKey);
    }

    /// <summary>
    /// Deposits extraction loot into the warehouse. Every item is normalized to its BASE state first (upgrades,
    /// enchantments, run-scoped growth and potion slot indices stripped; identity cards keep their saved props — see
    /// <see cref="NormalizeCard"/>). The durability of each card/relic copy is taken from the caller (the settlement
    /// algorithm pre-computes carried −1 vs full-for-new); a ≤0 value is clamped to 1 as a defensive invariant — the
    /// warehouse never stores a 0 sentinel. A single un-normalizable card (e.g. a corrupt saved-prop type) is skipped
    /// rather than aborting the whole deposit, so one bad loot item never swallows the settlement.
    /// 把撤离战利品追加存入仓库，进库前统一归一（升级/附魔/成长/栏位剥离，身份卡保留 Props）。每份牌/遗物的耐久取自已结算的
    /// 调用方（结算算法预先算出 携带-1 与 新货满耐久）；≤0 防御性收敛到 1——仓库从不存 0 哨兵。单张无法归一的卡（如非法保存
    /// 属性类型）跳过而非让整次存入失败，避免一张坏牌吞掉整局结算。
    /// </summary>
    public static void Deposit(IEnumerable<WarehouseCard>? cards, IEnumerable<WarehouseRelic>? relics,
        IEnumerable<SerializablePotion>? potions, int gold)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            data.Version++;

            if (cards != null)
            {
                foreach (WarehouseCard wc in cards)
                {
                    if (wc.Card == null)
                    {
                        Entry.Logger.Warn("WarehouseStore.Deposit: skipping null card copy.");
                        continue;
                    }

                    try
                    {
                        data.Cards.Add(new WarehouseCard
                        {
                            Card = NormalizeCard(wc.Card),
                            Durability = Math.Max(1, wc.Durability),
                        });
                    }
                    catch (Exception ex)
                    {
                        Entry.Logger.Warn($"WarehouseStore.Deposit: skipping un-normalizable card {wc.Card.Id}: {ex.Message}");
                    }
                }
            }

            if (relics != null)
            {
                foreach (WarehouseRelic wr in relics)
                {
                    if (wr.Relic == null)
                    {
                        Entry.Logger.Warn("WarehouseStore.Deposit: skipping null relic copy.");
                        continue;
                    }

                    try
                    {
                        data.Relics.Add(new WarehouseRelic
                        {
                            Relic = NormalizeRelic(wr.Relic),
                            Durability = Math.Max(1, wr.Durability),
                        });
                    }
                    catch (Exception ex)
                    {
                        Entry.Logger.Warn($"WarehouseStore.Deposit: skipping un-normalizable relic {wr.Relic.Id}: {ex.Message}");
                    }
                }
            }

            if (potions != null)
            {
                data.Potions.AddRange(potions.Select(NormalizePotion));
            }

            data.Gold = ClampGold(data.Gold + gold);
        });
        store.Save(ActiveKey);
    }

    /// <summary>
    /// Removes the carried items from this machine's warehouse (Tarkov-style: they are consumed on entry). Copies are
    /// matched by (id, durability) so the exact carried copies come out — the carry config is a snapshot of the
    /// warehouse, so the match always hits; a drift (mode toggle / console mutation) falls back to any copy of the id.
    /// Only called for the LOCAL player on each machine. 从本机仓库移除已携带进局的物品（进局即消耗）。按 (id, 耐久) 精确匹配
    /// 实际携带的那几份——携带配置是仓库快照，必然命中；漂移（切换/控制台改动）时回退按 id 删任意份。仅对本机本地玩家调用。
    /// </summary>
    public static void ConsumeCarried(CarryConfig carried)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            data.Version++;

            foreach (WarehouseCard carriedCard in carried.Cards)
            {
                int index = data.Cards.FindIndex(c => c.Card.Id == carriedCard.Card.Id && c.Durability == carriedCard.Durability);
                if (index < 0)
                {
                    index = data.Cards.FindIndex(c => c.Card.Id == carriedCard.Card.Id);
                }

                if (index >= 0)
                {
                    data.Cards.RemoveAt(index);
                }
            }

            foreach (WarehouseRelic carriedRelic in carried.Relics)
            {
                int index = data.Relics.FindIndex(r => r.Relic.Id == carriedRelic.Relic.Id && r.Durability == carriedRelic.Durability);
                if (index < 0)
                {
                    index = data.Relics.FindIndex(r => r.Relic.Id == carriedRelic.Relic.Id);
                }

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
        store.Save(ActiveKey);
    }

    /// <summary>
    /// Sells the given EXACT copies out of the warehouse (matched by id + durability, like <see cref="ConsumeCarried"/>)
    /// and credits the proceeds to the warehouse balance. The caller computes which copies to sell — the shop sells
    /// only non-carried copies, so it cannot reuse the plain <c>Remove*</c> helpers (which would scrape a carried copy).
    /// 把给定的"精确副本"从仓库卖出（按 id + 耐久匹配，同 ConsumeCarried）并把所得计入仓库余额。卖哪几份由调用方算好——
    /// 商店只卖未携带的副本，因此不能复用 Remove*（那会把携带中的那份也刮掉）。
    /// </summary>
    public static void Sell(IReadOnlyList<WarehouseCard>? cards, IReadOnlyList<WarehouseRelic>? relics,
        IReadOnlyList<SerializablePotion>? potions, int gold)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            data.Version++;

            if (cards != null)
            {
                foreach (WarehouseCard sold in cards)
                {
                    int index = data.Cards.FindIndex(c => c.Card.Id == sold.Card.Id && c.Durability == sold.Durability);
                    if (index >= 0)
                    {
                        data.Cards.RemoveAt(index);
                    }
                }
            }

            if (relics != null)
            {
                foreach (WarehouseRelic sold in relics)
                {
                    int index = data.Relics.FindIndex(r => r.Relic.Id == sold.Relic.Id && r.Durability == sold.Durability);
                    if (index >= 0)
                    {
                        data.Relics.RemoveAt(index);
                    }
                }
            }

            if (potions != null)
            {
                foreach (SerializablePotion sold in potions)
                {
                    int index = data.Potions.FindIndex(p => p.Id == sold.Id);
                    if (index >= 0)
                    {
                        data.Potions.RemoveAt(index);
                    }
                }
            }

            data.Gold = ClampGold(data.Gold + gold);
        });
        store.Save(ActiveKey);
    }

    /// <summary>
    /// Removes up to <paramref name="count"/> copies of the given card id from the warehouse, lowest durability first
    /// (scrap the most-worn gear first). Returns the number actually removed.
    /// 从仓库移除最多 count 张指定卡牌（最低耐久优先），返回实际移除数。</summary>
    public static int RemoveCards(ModelId id, int count) =>
        RemoveCopies(id, count, d => d.Cards, c => c.Card.Id, c => c.Durability);

    /// <summary>Removes up to <paramref name="count"/> copies of the given relic id from the warehouse, lowest
    /// durability first. Returns the number actually removed. 从仓库移除最多 count 个指定遗物（最低耐久优先），返回实际移除数。</summary>
    public static int RemoveRelics(ModelId id, int count) =>
        RemoveCopies(id, count, d => d.Relics, r => r.Relic.Id, r => r.Durability);

    /// <summary>Removes up to <paramref name="count"/> copies of the given potion id from the warehouse. Returns the number actually removed.
    /// 从仓库移除最多 count 瓶指定药水，返回实际移除数。</summary>
    public static int RemovePotions(ModelId id, int count) =>
        RemoveCopies(id, count, d => d.Potions, p => p.Id, _ => 0);

    /// <summary>Removes gold (never below zero). Returns the new warehouse balance. 移除金币（不会扣成负数），返回新余额。</summary>
    public static int RemoveGold(int amount)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        int balance = 0;
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            data.Version++;
            data.Gold = ClampGold(data.Gold - Math.Max(0, amount));
            balance = data.Gold;
        });
        store.Save(ActiveKey);
        return balance;
    }

    /// <summary>Removes up to <paramref name="count"/> copies matching <paramref name="id"/> from the list selected by
    /// <paramref name="listSelector"/>, ordering by durability ascending so the most-worn copies go first. The indexes
    /// are computed and consumed inside the Modify so the live list is the one actually touched.
    /// 从 listSelector 选出的列表移除最多 count 份匹配 id 的副本，按耐久升序先删最破的。下标在 Modify 内计算与消费，保证删的是活列表。</summary>
    private static int RemoveCopies<T>(ModelId id, int count, Func<WarehouseData, List<T>> listSelector,
        Func<T, ModelId?> idOf, Func<T, int> durabilityOf)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        int removed = 0;
        store.Modify<WarehouseData>(ActiveKey, data =>
        {
            data.Version++;
            List<T> list = listSelector(data);

            var indexes = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (idOf(list[i]) == id)
                {
                    indexes.Add(i);
                }
            }

            if (indexes.Count == 0)
            {
                return;
            }

            indexes.Sort((a, b) => durabilityOf(list[a]).CompareTo(durabilityOf(list[b])));

            // Remove highest index first so earlier removals don't shift later targets. 先删高下标，避免下标漂移。
            int take = Math.Min(count, indexes.Count);
            for (int k = take - 1; k >= 0; k--)
            {
                list.RemoveAt(indexes[k]);
                removed++;
            }
        });
        store.Save(ActiveKey);
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
    /// deposited into the warehouse, normalized like any other loot — granted on every clear, each copy at full
    /// durability. Returns the granted items so the settlement screen can fold them into the deposited loot.
    /// 通关奖励：通关搜打撤后，把该角色的整套初始牌组（含全部张数）与初始遗物按普通战利品归一化入账，每次通关都发放，
    /// 每份均为满耐久。返回本次发放的物品，供结算界面并入存入战利品展示。
    /// </summary>
    public static (List<WarehouseCard> Cards, List<WarehouseRelic> Relics) GrantCharacterCompletionReward(CharacterModel character)
    {
        string entry = character.Id.Entry;
        var grantedCards = new List<WarehouseCard>();
        var grantedRelics = new List<WarehouseRelic>();
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<WarehouseData>(ActiveKey, data =>
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
                    WarehouseCard wc = new()
                    {
                        Card = NormalizeCard(card.ToMutable().ToSerializable()),
                        Durability = MaxDurabilityForCard(card.Id),
                    };
                    data.Cards.Add(wc);
                    grantedCards.Add(wc);
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
                    WarehouseRelic wr = new()
                    {
                        Relic = NormalizeRelic(relic.ToMutable().ToSerializable()),
                        Durability = MaxDurabilityForRelic(),
                    };
                    data.Relics.Add(wr);
                    grantedRelics.Add(wr);
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"GrantCharacterCompletionReward: skipping starter relic {relic.Id}: {ex.Message}");
                }
            }
        });
        store.Save(ActiveKey);
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

    /// <summary>Max durability a card copy is granted by its rarity (the current setting; only new deposits read it).
    /// CardRarity.None/Event/Token/Status/Curse/Quest — and unresolvable ids — all fall to the 其他 bucket.
    /// 卡牌按稀有度可获得的满耐久（当前设置；只有新入库才读取）。None/Event/Token/Status/Curse/Quest 及解析不到一律归「其他」。</summary>
    public static int MaxDurabilityForCard(ModelId? id)
    {
        CardModel? model = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
        ExtractionSettings settings = ExtractionSettingsPage.Current;
        return model?.Rarity switch
        {
            CardRarity.Basic => settings.CardDurabilityBasic,
            CardRarity.Common => settings.CardDurabilityCommon,
            CardRarity.Uncommon => settings.CardDurabilityUncommon,
            CardRarity.Rare => settings.CardDurabilityRare,
            CardRarity.Ancient => settings.CardDurabilityAncient,
            _ => settings.CardDurabilityOther,
        };
    }

    /// <summary>Max durability a relic copy is granted (all relics share one setting). 遗物的满耐久（统一设置）。</summary>
    public static int MaxDurabilityForRelic() => ExtractionSettingsPage.Current.RelicDurability;

    /// <summary>
    /// Backfills ≤0 (legacy 0-sentinel) durability on a carry config's copies so a pre-durability saved carry
    /// decrements from full at extraction instead of breaking every copy. Idempotent — only touches ≤0.
    /// 把携带配置里 ≤0（旧档 0 哨兵）的耐久回填为满，避免旧版携带在撤离时全部按「1→0 战损」处理。幂等——只动 ≤0。
    /// </summary>
    public static void BackfillCarryDurability(CarryConfig config)
    {
        foreach (WarehouseCard wc in config.Cards)
        {
            if (wc.Durability <= 0)
            {
                wc.Durability = MaxDurabilityForCard(wc.Card.Id);
            }
        }

        foreach (WarehouseRelic wr in config.Relics)
        {
            if (wr.Durability <= 0)
            {
                wr.Durability = MaxDurabilityForRelic();
            }
        }
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
