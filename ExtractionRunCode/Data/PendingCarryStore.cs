using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;

namespace ExtractionRun.Data;

/// <summary>
/// The persistent "pending" carry config a player sets in the warehouse hub before a lobby/run exists. When a lobby
/// becomes active, <c>ExtractionCarrySync</c> stages this into the run's <c>PlayerRunSavedData</c> (MP-synced), so it
/// reaches the run on every machine. 仓库大厅里设置的待发携带配置；进入大厅时被暂存进局内 RunSavedData。
/// </summary>
public static class PendingCarryStore
{
    public const string DataKey = "pendingCarry";

    /// <summary>Registers the pending-carry slot. Must run inside <c>BeginModDataRegistration</c>.</summary>
    public static void Register()
    {
        ModDataStore.For(Entry.ModId).Register(
            key: DataKey,
            fileName: "pending_carry.json",
            scope: SaveScope.Profile,
            defaultFactory: () => new CarryConfig(),
            autoCreateIfMissing: true);
    }

    /// <summary>The pending carry config for the current profile. 当前存档的待发携带配置。</summary>
    public static CarryConfig Current => RitsuLibFramework.GetDataStore(Entry.ModId).Get<CarryConfig>(DataKey);

    /// <summary>
    /// Returns a detached copy of the current pending carry for in-memory editing. The hub mutates its own copy and
    /// only writes it back via <see cref="Set"/> on confirm/start — so closing or backing out never leaks edits into
    /// the store's live instance (which would otherwise stage an unconfirmed loadout on the next join). 返回当前待发携带
    /// 的独立副本供界面编辑；界面只在自己的副本上改动，确认/开跑时才 Set 写回，返回/关闭不会把未确认的改动泄漏进活实例。
    /// </summary>
    public static CarryConfig Snapshot()
    {
        CarryConfig current = Current;
        return new CarryConfig
        {
            Cards = current.Cards.ToList(),
            Relics = current.Relics.ToList(),
            Potions = current.Potions.ToList(),
            Gold = current.Gold,
        };
    }

    /// <summary>Overwrites and persists the pending carry config. 覆盖并持久化待发携带配置。</summary>
    public static void Set(CarryConfig config)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<CarryConfig>(DataKey, data =>
        {
            // Snapshot the source lists before clearing so Set(config) stays correct even if config aliases data.
            // 先快照源列表再清空，保证 config 与 data 为同一实例时也能正确写回。
            List<WarehouseCard> cards = config.Cards.ToList();
            List<WarehouseRelic> relics = config.Relics.ToList();
            List<SerializablePotion> potions = config.Potions.ToList();
            int gold = config.Gold;

            data.Cards.Clear();
            data.Cards.AddRange(cards);

            data.Relics.Clear();
            data.Relics.AddRange(relics);

            data.Potions.Clear();
            data.Potions.AddRange(potions);

            data.Gold = gold;
        });
        store.Save(DataKey);
    }

    /// <summary>Clears the pending carry config (nothing carried). 清空待发携带配置。</summary>
    public static void Clear()
    {
        Set(new CarryConfig());
    }

    /// <summary>
    /// Clamps the carry so it never references more of an item than the warehouse holds, and clamps carried gold to the
    /// warehouse balance. Called after warehouse-shrinking console commands (reset / remove) and on durability-mode
    /// switches: without it, a carried item that no longer exists (or is no longer sufficiently stocked) in the
    /// warehouse would be injected at run start while <see cref="WarehouseStore.ConsumeCarried"/> skips the missing
    /// copies — a free-item dupe.
    /// 把携带收敛到不超过仓库存量：删仓/重置后与耐久模式切换时调用；否则携带中已不在仓库（或超出存量）的物品会在开跑时被全部注入、
    /// 消耗却只跳过缺失——白嫖。
    /// </summary>
    public static void RevalidateAgainst(WarehouseData warehouse)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<CarryConfig>(DataKey, data =>
        {
            data.Cards = ClampCarry(data.Cards, c => c.Card.Id, CountsBy(warehouse.Cards, c => c.Card.Id));
            data.Relics = ClampCarry(data.Relics, r => r.Relic.Id, CountsBy(warehouse.Relics, r => r.Relic.Id));
            data.Potions = ClampCarry(data.Potions, p => p.Id, CountsBy(warehouse.Potions, p => p.Id));
            data.Gold = Math.Min(Math.Max(0, data.Gold), Math.Max(0, warehouse.Gold));
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Re-syncs each carried copy's durability to the given warehouse's copies (lowest durability first, in carry
    /// order). Called on durability-mode switches: the pending carry is a snapshot of one mode's warehouse, and after
    /// the switch the copied durability values would otherwise be stale — inflating the decrement at extraction (a
    /// full-durability OFF-mode copy carried into an ON run would come back at max−1 instead of its real value −1).
    /// 把每份携带副本的耐久重新对齐到给定仓库的副本（按耐久升序、依携带顺序）。耐久模式切换时调用：携带是某个模式仓库的
    /// 快照，切换后其耐久值会过期——否则撤离时按过期值递减（把 OFF 模式满耐久副本带进 ON 局会带回 max−1 而非真实值 −1）。
    /// </summary>
    public static void RevalidateDurability(WarehouseData warehouse)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<CarryConfig>(DataKey, data =>
        {
            data.Cards = RemapDurability(data.Cards, warehouse.Cards,
                static c => c.Card.Id, static c => c.Durability,
                static (c, d) => new WarehouseCard { Card = c.Card, Durability = d });
            data.Relics = RemapDurability(data.Relics, warehouse.Relics,
                static r => r.Relic.Id, static r => r.Durability,
                static (r, d) => new WarehouseRelic { Relic = r.Relic, Durability = d });
        });
        store.Save(DataKey);
    }

    /// <summary>
    /// Rebuilds a carried copy list's durability in place: for each carried copy (in order), take the durability of the
    /// next lowest-durability warehouse copy of the same id. A carried copy whose id has no warehouse stock left is kept
    /// as-is (RevalidateAgainst already clamped counts, so this only guards corrupt state).
    /// 原地重建携带副本的耐久：按顺序，每份携带副本取仓库中同 id 下一份最低耐久副本的耐久值。仓库已无同 id 库存时保留原值
    /// （RevalidateAgainst 已按数量钳制，这里只防御脏状态）。
    /// </summary>
    private static List<T> RemapDurability<T>(List<T> carried, List<T> warehouse, Func<T, ModelId?> idOf,
        Func<T, int> durabilityOf, Func<T, int, T> withDurability)
    {
        var enumerators = new Dictionary<ModelId, IEnumerator<T>>();
        foreach (IGrouping<ModelId?, T> group in warehouse.GroupBy(idOf))
        {
            if (group.Key is ModelId id)
            {
                enumerators[id] = group.OrderBy(durabilityOf).GetEnumerator();
            }
        }

        var result = new List<T>(carried.Count);
        foreach (T copy in carried)
        {
            if (idOf(copy) is ModelId id && enumerators.TryGetValue(id, out IEnumerator<T>? it) && it.MoveNext())
            {
                result.Add(withDurability(copy, durabilityOf(it.Current)));
            }
            else
            {
                result.Add(copy);
            }
        }

        return result;
    }

    private static Dictionary<ModelId, int> CountsBy<T>(IEnumerable<T> items, Func<T, ModelId?> idSelector) where T : class
    {
        var counts = new Dictionary<ModelId, int>();
        foreach (T item in items)
        {
            if (idSelector(item) is ModelId id)
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        return counts;
    }

    /// <summary>Keeps the first <c>stock</c> copies of each id (null-id entries are dropped as corrupt). 每 id 只保留 stock 份。</summary>
    private static List<T> ClampCarry<T>(IEnumerable<T> items, Func<T, ModelId?> idSelector, Dictionary<ModelId, int> stock)
        where T : class
    {
        var kept = new List<T>();
        var used = new Dictionary<ModelId, int>();
        foreach (T item in items)
        {
            if (idSelector(item) is not ModelId id)
            {
                continue;
            }

            int usedCount = used.GetValueOrDefault(id);
            if (usedCount < stock.GetValueOrDefault(id))
            {
                kept.Add(item);
                used[id] = usedCount + 1;
            }
        }

        return kept;
    }
}
