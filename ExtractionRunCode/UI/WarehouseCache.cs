using Godot;
using ExtractionRun.Data;

namespace ExtractionRun.UI;

/// <summary>
/// Module-level display cache for the warehouse hub. Groups every variety (base-card / relic / potion) once per
/// (warehouse instance reference, version) and preloads all item art in the background via
/// <see cref="ResourceLoader.LoadThreadedRequest"/>. Survives hub close/reopen within a session, so the second open
/// costs nothing; invalidates automatically when <see cref="WarehouseStore"/> bumps <see cref="WarehouseData.Version"/>
/// (deposit / consume / seed / migration) or when the save slot changes (RitsuLib swaps the root instance, breaking
/// reference equality — see ModDataStore.Get docs).
/// 仓库大厅的模块级展示缓存：按（仓库实例引用, 版本号）一次性分组全部牌种（基础卡/遗物/药水），并用 LoadThreadedRequest 后台预载全部贴图。
/// 缓存跨大厅开合存活（同会话第二次打开零成本）；Deposit/ConsumeCarried/种子/迁移会自增 Version，或切换存档位导致实例被替换，都会自动失效重建。
/// </summary>
public static class WarehouseCache
{
    private static WarehouseData? _key;
    private static int _version = -1;

    private static List<ExtractionItemTiles.CardGroup> _cards = new();
    private static List<ExtractionItemTiles.RelicGroup> _relics = new();
    private static List<ExtractionItemTiles.PotionGroup> _potions = new();

    // ----- Async art preload state 异步贴图预载状态 -----
    /// <summary>Frames an in-flight request may take before it is dropped (guards against stuck paths). 在途请求的帧数上限，防止卡死的路径永不释放。</summary>
    private const int MaxInFlightTicks = 600;

    private static readonly Dictionary<string, Texture2D> Loaded = new();
    private static readonly HashSet<string> Requested = new();
    private static readonly Queue<string> Pending = new();
    private static readonly Dictionary<string, int> InFlight = new();

    public static IReadOnlyList<ExtractionItemTiles.CardGroup> Cards => _cards;

    public static IReadOnlyList<ExtractionItemTiles.RelicGroup> Relics => _relics;

    public static IReadOnlyList<ExtractionItemTiles.PotionGroup> Potions => _potions;

    /// <summary>
    /// Rebuilds the grouped metadata when the warehouse instance or its version changed; otherwise a no-op. Art is
    /// grouped with <c>loadArt: false</c> so the hub never synchronously decodes a portrait — every texture comes
    /// through <see cref="Resolve"/> once the background preload finished. 版本/实例变化时重建分组元数据，否则空操作。
    /// 分组时不解析贴图（loadArt:false），所有贴图经 Resolve 在后台预载完成后按需提供。
    /// </summary>
    public static void Ensure(WarehouseData warehouse)
    {
        if (_key == warehouse && _version == warehouse.Version)
        {
            return;
        }

        _key = warehouse;
        _version = warehouse.Version;
        _cards = ExtractionItemTiles.GroupCards(warehouse.Cards, loadArt: false);
        _relics = ExtractionItemTiles.GroupRelics(warehouse.Relics, loadArt: false);
        _potions = ExtractionItemTiles.GroupPotions(warehouse.Potions, loadArt: false);
        ResetPrewarm();
    }

    /// <summary>
    /// Returns the loaded texture for an art path, or null if it hasn't finished preloading (or has no art). 返回已加载的
    /// 贴图；未加载完（或无贴图）返回 null。
    /// </summary>
    public static Texture2D? Resolve(string? path)
    {
        return path != null && Loaded.TryGetValue(path, out Texture2D? texture) ? texture : null;
    }

    /// <summary>
    /// Advances the background preload: submits up to <paramref name="perFrame"/> new threaded requests and polls
    /// in-flight ones. Returns true when at least one texture finished loading this frame (caller refreshes the visible
    /// tiles). 推进后台预载：每帧最多提交 perFrame 个新请求并轮询在途请求；本帧有新贴图加载完成时返回 true。
    /// </summary>
    public static bool Tick(int perFrame)
    {
        bool loadedAny = false;

        int submitted = 0;
        while (Pending.Count > 0 && submitted < perFrame)
        {
            string path = Pending.Dequeue();
            if (!Requested.Add(path))
            {
                continue;
            }

            try
            {
                ResourceLoader.LoadThreadedRequest(path);
                InFlight[path] = 0;
                submitted++;
            }
            catch (Exception)
            {
                // Unloadable path (corrupt entry) — drop it silently.
            }
        }

        // Poll in-flight loads; drop any that outlive the frame budget.
        foreach (string path in InFlight.Keys.ToList())
        {
            int age = InFlight[path] + 1;
            if (age > MaxInFlightTicks)
            {
                InFlight.Remove(path);
                continue;
            }

            InFlight[path] = age;

            ResourceLoader.ThreadLoadStatus status;
            try
            {
                status = ResourceLoader.LoadThreadedGetStatus(path);
            }
            catch (Exception)
            {
                status = ResourceLoader.ThreadLoadStatus.Failed;
            }

            if (status == ResourceLoader.ThreadLoadStatus.Loaded)
            {
                try
                {
                    if (ResourceLoader.LoadThreadedGet(path) is Texture2D texture)
                    {
                        Loaded[path] = texture;
                    }
                }
                catch (Exception)
                {
                    // Ignore: the tile simply keeps its empty art well.
                }

                InFlight.Remove(path);
                loadedAny = true;
            }
            else if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
            {
                InFlight.Remove(path);
            }
        }

        return loadedAny;
    }

    /// <summary>Queues every variety's art path for background loading (deduped, missing paths skipped). 将所有牌种的贴图路径排入预载队列。</summary>
    private static void ResetPrewarm()
    {
        Loaded.Clear();
        Requested.Clear();
        Pending.Clear();
        InFlight.Clear();

        var seen = new HashSet<string>();
        foreach (string path in AllArtPaths())
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(path) || !ResourceLoader.Exists(path))
            {
                continue;
            }

            Pending.Enqueue(path);
        }
    }

    private static IEnumerable<string> AllArtPaths()
    {
        foreach (ExtractionItemTiles.CardGroup group in _cards)
        {
            yield return group.PortraitPath;
        }

        foreach (ExtractionItemTiles.RelicGroup group in _relics)
        {
            yield return group.IconPath;
        }

        foreach (ExtractionItemTiles.PotionGroup group in _potions)
        {
            yield return group.ImagePath;
        }
    }
}
