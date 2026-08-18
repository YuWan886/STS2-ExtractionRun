using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ExtractionRun.Data;

/// <summary>
/// Persistent challenge state + the daily roll. The daily pool is rolled once per local calendar day (first read that
/// day), <see cref="DailySlotCount"/> distinct ids drawn from the registry's daily pool; <see cref="RefreshDaily"/> re-rolls
/// it on demand (console command). Daily challenges are re-selectable and grant their reward on every clear — no
/// completion gate (grill-locked: unlimited farming is the intended design).
/// 挑战状态 + 每日 roll。每日池按本地日历日 daily 一次（当天首次读取时），从注册表每日池抽取五个互不重复的 id；
/// RefreshDaily 可即时重 roll（控制台指令）。每日挑战可重复选择、每次通关都发奖励——无完成闸门（grill 锁定：无限刷即设计本意）。
/// </summary>
public static class ChallengeStore
{
    public const string DataKey = "challenge";

    /// <summary>Daily slot count. 每日挑战槽位数。</summary>
    public const int DailySlotCount = 5;

    /// <summary>Registers the challenge slot. Must run inside <c>BeginModDataRegistration</c>.</summary>
    public static void Register()
    {
        ModDataStore.For(Entry.ModId).Register(
            key: DataKey,
            fileName: "challenge.json",
            scope: SaveScope.Profile,
            defaultFactory: () => new ChallengeData(),
            autoCreateIfMissing: true);
    }

    /// <summary>The live challenge state for the current profile. 当前存档的挑战状态实例。</summary>
    public static ChallengeData Current => RitsuLibFramework.GetDataStore(Entry.ModId).Get<ChallengeData>(DataKey);

    /// <summary>Local calendar date the daily roll keys on (mirrors the shop's day rollover). 每日 roll 依据的本地日历日期。</summary>
    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// Rolls a fresh daily pool on the first read of a new day (idempotent within a day). Called on the challenge page
    /// open. 新一天的首次读取时全量重 roll 每日池（同日幂等）。挑战页打开时调用。
    /// </summary>
    public static void EnsureDailyRolled()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<ChallengeData>(DataKey, data =>
        {
            NormalizePersistentState(data);
            string today = Today();
            bool catalogChanged = !string.IsNullOrEmpty(data.DailyCatalogHash)
                && data.DailyCatalogHash != ChallengeRegistry.CatalogHash;
            if (data.DailyDate != today)
            {
                data.DailyDate = today;
                data.DailyRollRevision = 0;
                data.DailyRollSeed = CreateDailySeed(today, data.DailyRollRevision);
                data.DailyIds = RollDailyIds(seed: data.DailyRollSeed);
            }
            else
            {
                // Keep today's valid offers stable across a catalog upgrade; only invalid/removed ids are backfilled.
                if (data.DailyRollSeed == 0)
                {
                    data.DailyRollSeed = CreateDailySeed(today, data.DailyRollRevision);
                }
                data.DailyIds = RollDailyIds(data.DailyIds, data.DailyRollSeed);
                if (catalogChanged)
                {
                    Entry.Logger.Info("ChallengeStore: catalog changed; preserved valid daily offers and backfilled gaps.");
                }
            }

            data.CatalogSchemaVersion = ChallengeRegistry.CatalogSchemaVersion;
            data.DailyCatalogHash = ChallengeRegistry.CatalogHash;
        });
        store.Save(DataKey);
    }

    /// <summary>Five distinct daily ids drawn from the registry's daily pool (fewer than slots → the whole pool).
    /// 从注册表每日池抽取五个互不重复的 id（少于槽位 → 整池）。</summary>
    private static List<string> RollDailyIds(IEnumerable<string>? preservedIds = null, int seed = 0)
    {
        List<string> pool = ChallengeRegistry.Dailies.Select(d => d.Id).ToList();
        List<string> result = ChallengeSelectionService.NormalizeRunIds(preservedIds).Ids
            .Where(ChallengeRegistry.IsDaily)
            .Take(DailySlotCount)
            .ToList();
        if (pool.Count <= DailySlotCount)
        {
            // Fewer/equal entries than slots: every daily challenge is on offer today (distinct by construction).
            return pool;
        }

        // More entries than slots: draw distinct ids without replacement. Preserve still-valid old slots when a catalog
        // migration removed an id, so updating the mod does not reshuffle an entire day for the player.
        int missing = DailySlotCount - result.Count;
        if (missing > 0)
        {
            List<string> candidates = pool.Where(id => !result.Contains(id)).ToList();
            var random = new Random(seed);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                (candidates[i], candidates[swap]) = (candidates[swap], candidates[i]);
            }
            result.AddRange(candidates.Take(missing));
        }

        return result;
    }

    /// <summary>Re-rolls the day's daily pool immediately (console command). The date key is unchanged, so a later
    /// refresh within the same day re-rolls again — unlimited by design (grill-locked). The caller removes any
    /// still-selected id that fell out of the new pool. 立即重 roll 当日每日池（控制台指令）。日期键不变，同日可反复刷新——无限刷是
    /// 既定设计。被换出池子的已选 id 由调用方从草稿移除。</summary>
    public static void RefreshDaily()
    {
        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<ChallengeData>(DataKey, data =>
        {
            NormalizePersistentState(data);
            string today = Today();
            if (data.DailyDate != today)
            {
                data.DailyDate = today;
                data.DailyRollRevision = 0;
            }
            else
            {
                data.DailyRollRevision++;
            }
            data.DailyRollSeed = CreateDailySeed(today, data.DailyRollRevision);
            data.DailyIds = RollDailyIds(seed: data.DailyRollSeed);
            data.CatalogSchemaVersion = ChallengeRegistry.CatalogSchemaVersion;
            data.DailyCatalogHash = ChallengeRegistry.CatalogHash;
        });
        store.Save(DataKey);
    }

    /// <summary>True when the local day's pool contains <paramref name="id"/>. 本地当日池是否含该 id。</summary>
    public static bool ContainsDaily(string id) => Current.DailyIds.Contains(id);

    /// <summary>Total clears for a daily challenge (0 = never cleared). Daily clears are tracked for display only and
    /// never make the challenge unavailable. 每日挑战累计通关次数（0=从未通关）；仅供展示，不会令挑战不可选。</summary>
    public static int GetDailyClearCount(string id) =>
        ChallengeRegistry.TryResolveId(id, out string resolvedId)
        && Current.DailyClearCounts.TryGetValue(resolvedId, out int count) && count > 0 ? count : 0;

    /// <summary>Total clears for a permanent challenge (0 = never cleared). A legacy save that predates counting shows
    /// 1 for a marked entry. 常驻挑战累计通关次数（0=从未通关）。计数功能前的旧存档对已标记条目显示 1。</summary>
    public static int GetPermanentClearCount(string id)
    {
        if (!ChallengeRegistry.TryResolveId(id, out string resolvedId))
        {
            return 0;
        }

        ChallengeData data = Current;
        if (data.PermanentClearCounts.TryGetValue(resolvedId, out int count) && count > 0)
        {
            return count;
        }
        return data.PermanentCleared.Contains(resolvedId) ? 1 : 0;
    }

    /// <summary>Total clears for any registered challenge. 任意已注册挑战的累计通关次数。</summary>
    public static int GetClearCount(string id) =>
        ChallengeRegistry.IsDaily(id) ? GetDailyClearCount(id) : GetPermanentClearCount(id);

    /// <summary>True when a permanent challenge was cleared at least once (✓). 常驻挑战是否通关过（打勾）。</summary>
    public static bool IsPermanentCleared(string id) => GetPermanentClearCount(id) > 0;

    /// <summary>Records a permanent challenge clear — adds the id to the cleared set and increments its counter
    /// (the per-clear count accumulates). 记录一次常驻挑战通关：把 id 记入通关集合并让计数 +1（通关次数累积）。</summary>
    public static void MarkPermanentCleared(string id)
    {
        if (!ChallengeRegistry.TryResolveId(id, out string resolvedId)
            || ChallengeRegistry.Get(resolvedId)?.Kind != ChallengeKind.Permanent)
        {
            Entry.Logger.Warn($"ChallengeStore: ignored unknown/non-permanent completion id '{id}'.");
            return;
        }

        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<ChallengeData>(DataKey, data =>
        {
            NormalizePersistentState(data);
            if (!data.PermanentCleared.Contains(resolvedId))
            {
                data.PermanentCleared.Add(resolvedId);
            }
            data.PermanentClearCounts[resolvedId] = data.PermanentClearCounts.TryGetValue(resolvedId, out int count)
                ? count + 1
                : 1;
        });
        store.Save(DataKey);
    }

    /// <summary>Records one daily challenge clear. This updates only the display counter; dailies remain re-selectable.
    /// 记录一次每日挑战通关：仅更新展示次数，每日挑战仍可重复选择。</summary>
    public static void MarkDailyCleared(string id)
    {
        if (!ChallengeRegistry.TryResolveId(id, out string resolvedId)
            || ChallengeRegistry.Get(resolvedId)?.Kind != ChallengeKind.Daily)
        {
            Entry.Logger.Warn($"ChallengeStore: ignored unknown/non-daily completion id '{id}'.");
            return;
        }

        var store = RitsuLibFramework.GetDataStore(Entry.ModId);
        store.Modify<ChallengeData>(DataKey, data =>
        {
            NormalizePersistentState(data);
            data.DailyClearCounts[resolvedId] = data.DailyClearCounts.TryGetValue(resolvedId, out int count)
                ? count + 1
                : 1;
        });
        store.Save(DataKey);
    }

    /// <summary>Records a clear for the challenge's registered kind. 按挑战注册类型记录一次通关。</summary>
    public static void MarkCleared(string id)
    {
        ChallengeDef? definition = ChallengeRegistry.Get(id);
        if (definition == null)
        {
            Entry.Logger.Warn($"ChallengeStore: ignored unknown completion id '{id}'.");
            return;
        }

        if (definition.Kind == ChallengeKind.Daily)
        {
            MarkDailyCleared(definition.Id);
        }
        else
        {
            MarkPermanentCleared(definition.Id);
        }
    }

    /// <summary>Persists the challenge state (used after any external mutation). 持久化挑战状态。</summary>
    public static void Persist()
    {
        RitsuLibFramework.GetDataStore(Entry.ModId).Save(DataKey);
    }

    /// <summary>Repairs legacy aliases, duplicate ids and state left behind by removed challenge definitions.</summary>
    private static void NormalizePersistentState(ChallengeData data)
    {
        data.DailyIds = ChallengeSelectionService.NormalizeRunIds(data.DailyIds).Ids
            .Where(ChallengeRegistry.IsDaily)
            .Take(DailySlotCount)
            .ToList();
        data.DailyClearCounts = NormalizeCounts(data.DailyClearCounts, ChallengeKind.Daily);
        data.PermanentClearCounts = NormalizeCounts(data.PermanentClearCounts, ChallengeKind.Permanent);
        data.PermanentCleared = ChallengeSelectionService.NormalizeRunIds(data.PermanentCleared).Ids
            .Where(id => ChallengeRegistry.Get(id)?.Kind == ChallengeKind.Permanent)
            .ToList();

        foreach (string id in data.PermanentClearCounts.Keys)
        {
            if (!data.PermanentCleared.Contains(id))
            {
                data.PermanentCleared.Add(id);
            }
        }
    }

    private static Dictionary<string, int> NormalizeCounts(Dictionary<string, int>? source, ChallengeKind kind)
    {
        var normalized = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string rawId, int count) in source ?? new Dictionary<string, int>())
        {
            if (count <= 0 || !ChallengeRegistry.TryResolveId(rawId, out string id)
                || ChallengeRegistry.Get(id)?.Kind != kind)
            {
                continue;
            }

            normalized[id] = normalized.GetValueOrDefault(id) + count;
        }

        return normalized;
    }

    private static int CreateDailySeed(string date, int revision)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{date}|{ChallengeRegistry.CatalogHash}|{revision}"));
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
