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

    /// <summary>Overwrites and persists the pending carry config. 覆盖并持久化待发携带配置。</summary>
    public static void Set(CarryConfig config)
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<CarryConfig>(DataKey, data =>
        {
            
            List<SerializableCard> cards = config.Cards.ToList();
            List<SerializableRelic> relics = config.Relics.ToList();
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
}
