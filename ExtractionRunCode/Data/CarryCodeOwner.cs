using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib.Content;

namespace ExtractionRun.Data;

/// <summary>
/// Resolves which mod owns a carried item (for the gear code's bracket annotation) and maps normalized mod-id stems to
/// loaded-mod display names (for the import dialog's missing-mod report). Resolution order: RitsuLib's content registry
/// (<see cref="ModContentRegistry.TryGetOwnerModId"/>) → matching the model's assembly to a loaded mod (attribution for
/// any framework, e.g. YuWanCard/BaseLib/plain-Harmony content, regardless of public-entry convention) → matching the
/// entry's first underscore-segment to a loaded mod's normalized id (best-effort when the model itself can't be resolved,
/// or for content whose assembly isn't associated). Items that match nothing are treated as base content (no annotation).
/// The loaded-mod table is built once (mods don't change mid-session) and cached for the process.
/// 解析物品的归属 mod（用于战备码标注）与规范化 mod id → 显示名（用于导入的缺 mod 报告）。解析顺序：RitsuLib 内容注册表 →
/// 按模型程序集匹配已加载 mod（覆盖任何框架内容，如 YuWanCard/BaseLib/纯 Harmony，不依赖公开 entry 约定）→ entry 首段匹配
/// 已加载 mod 的规范化 id（模型本身解析不到时的兜底）。匹配不到的按基础内容处理。已加载 mod 表只构建一次并缓存。
/// </summary>
public static class CarryCodeOwner
{
    private static readonly BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static List<LoadedModInfo>? _loadedMods;

    /// <summary>
    /// Resolves the normalized mod-id stem (bracket annotation) for an item id, or null for base content.
    /// 解析物品归属的规范化 mod id，基础内容返回 null。
    /// </summary>
    public static string? ResolveOwnerStem(CarryCodec.ItemKind kind, ModelId id) =>
        ResolveSource(kind, id) is { IsMod: true } src ? src.ModStem : null;

    /// <summary>
    /// Resolves an item's content source: a loaded mod (normalized stem), base content, or unknown (a model that can't
    /// be resolved — e.g. content from an uninstalled mod — and whose entry prefix matches no loaded mod). Resolution
    /// order: RitsuLib registry → model-assembly match → entry-prefix heuristic, matching the gear-code attribution.
    /// Used by the warehouse hub's 内容来源 filter. 解析物品的内容来源：某已加载 mod（规范化 stem）/ 原版 / 未知（模型解析不到——
    /// 如来自已卸载 mod——且 entry 首段也不匹配任何已加载 mod）。解析顺序：注册表 → 程序集 → 首段启发式，与战备码归属一致。
    /// </summary>
    public static ContentSource ResolveSource(CarryCodec.ItemKind kind, ModelId id)
    {
        AbstractModel? model = ResolveModel(kind, id);
        if (model != null)
        {
            if (ModContentRegistry.TryGetOwnerModId(model.GetType(), out string modId) &&
                NormalizeStem(modId) is string registeredStem)
            {
                return ContentSource.Mod(registeredStem);
            }

            Assembly modelAssembly = model.GetType().Assembly;
            foreach (LoadedModInfo info in LoadedMods)
            {
                if (BelongsToMod(modelAssembly, info))
                {
                    return ContentSource.Mod(info.Stem);
                }
            }
        }

        // Entry-prefix heuristic (first underscore/dash segment == a loaded mod's id) covers model-resolvable content
        // with no registry/assembly association, and unloaded-mod leftovers whose id still names their mod. 首段启发式覆盖
        // 模型解析得到但无注册/程序集归属，或已卸载 mod 残留（id 仍带 mod 名首段）的内容。
        if (MatchPrefixStem(id.Entry) is string prefixStem)
        {
            return ContentSource.Mod(prefixStem);
        }

        return model != null ? ContentSource.Base : ContentSource.Unknown;
    }

    private static AbstractModel? ResolveModel(CarryCodec.ItemKind kind, ModelId id) => kind switch
    {
        CarryCodec.ItemKind.Card => ModelDb.GetByIdOrNull<CardModel>(id),
        CarryCodec.ItemKind.Relic => ModelDb.GetByIdOrNull<RelicModel>(id),
        _ => ModelDb.GetByIdOrNull<PotionModel>(id),
    };

    /// <summary>
    /// Entry-prefix heuristic against a mod's candidate prefixes: the normalized stem (<c>WHAT_IF_RELICS</c>), the
    /// compact id (<c>WHATIFRELICS</c> — YuWanCard entries are <c>YUWANCARD-...</c>), and the raw manifest id. Matches an
    /// entry starting with <c>{prefix}_</c> (RitsuLib compound entries) or its first <c>_</c>/<c>-</c> segment (base
    /// <c>MODID_X</c> and YuWanCard <c>MODID-X</c> conventions). 首段启发式：按 mod 候选前缀（规范化 stem、紧凑 id、原始
    /// manifest id）命中「{prefix}_ 开头」或首个 _/- 段（基础 MODID_X 与 YuWanCard MODID-X 两种惯例）。
    /// </summary>
    private static string? MatchPrefixStem(string entry)
    {
        int sep = entry.IndexOfAny(new[] { '_', '-' });
        string segment = sep < 0 ? entry : entry.Substring(0, sep);
        foreach (LoadedModInfo info in LoadedMods)
        {
            if (segment.Length > 0 && info.Prefixes.Contains(segment))
            {
                return info.Stem;
            }

            foreach (string prefix in info.Prefixes)
            {
                if (prefix.Length > 0 && entry.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
                {
                    return info.Stem;
                }
            }
        }

        return null;
    }

    /// <summary>True when a mod whose normalized id equals <paramref name="stem"/> is loaded. 是否存在规范化 id 等于 stem 的已加载 mod。</summary>
    public static bool IsModLoaded(string stem) => LoadedMods.Any(m => m.Stem == stem);

    /// <summary>Display name for a mod stem: the loaded mod's manifest name, or the stem itself when not loaded.
    /// mod stem 的显示名：已加载则用清单名，否则返回 stem 本身。</summary>
    public static string ResolveModDisplayName(string stem)
    {
        foreach (LoadedModInfo info in LoadedMods)
        {
            if (info.Stem == stem)
            {
                return info.DisplayName;
            }
        }

        return stem;
    }

    private static string? NormalizeStem(string modId)
    {
        try
        {
            string stem = ModContentRegistry.NormalizePublicStem(modId);
            return stem.Length > 0 ? stem : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<LoadedModInfo> LoadedMods
    {
        get
        {
            if (_loadedMods == null)
            {
                var list = new List<LoadedModInfo>();
                try
                {
                    foreach (Mod mod in ModManager.Mods)
                    {
                        try
                        {
                            // Only actually-loaded mods own content: a failed/disabled mod's content is unresolvable and
                            // must fall to Unknown, not attribute to a mod that isn't really there. 只把真正加载成功的 mod 计入
                            // 归属表：加载失败/被禁用的 mod 内容解析不到，应落 Unknown，而不是归属给一个其实没加载的 mod。
                            if (!IsLoaded(mod))
                            {
                                continue;
                            }

                            string? id = mod.manifest?.id;
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            if (NormalizeStem(id) is not string stem)
                            {
                                continue;
                            }

                            string? name = mod.manifest?.name;
                            list.Add(new LoadedModInfo
                            {
                                Stem = stem,
                                DisplayName = string.IsNullOrWhiteSpace(name) ? id : name,
                                Path = ModPath(mod),
                                Assemblies = ModAssemblies(mod),
                                Prefixes = BuildPrefixes(id, stem),
                            });
                        }
                        catch (Exception)
                        {
                            // A single unreadable mod must not abort the whole enumeration.
                        }
                    }
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"CarryCodeOwner: failed to enumerate loaded mods: {ex.Message}");
                }

                _loadedMods = list;
            }

            return _loadedMods;
        }
    }

    /// <summary>
    /// Reads a mod's assembly list across game versions (0.108+ exposes <c>assemblies</c>; 0.107.1 has a single
    /// <c>assembly</c>). Defensive: any drift just yields no match and falls through to the entry-prefix heuristic.
    /// 跨版本读取 mod 的程序集列表（0.108+ 为 assemblies，0.107.1 为单个 assembly）。防御式：读取失败即无匹配，回退到 entry 首段。
    /// </summary>
    private static IReadOnlyList<Assembly> ModAssemblies(Mod mod)
    {
        try
        {
            if (typeof(Mod).GetField("assemblies", FieldFlags)?.GetValue(mod) is IEnumerable<Assembly> list)
            {
                return list.Where(a => a != null).ToArray();
            }
        }
        catch (Exception)
        {
            // Fall through to the single-assembly field below.
        }

        try
        {
            if (typeof(Mod).GetField("assembly", FieldFlags)?.GetValue(mod) is Assembly single)
            {
                return single == null ? Array.Empty<Assembly>() : new[] { single };
            }
        }
        catch (Exception)
        {
            // No assembly info readable — caller falls back to the entry-prefix heuristic.
        }

        return Array.Empty<Assembly>();
    }

    /// <summary>
    /// True when <paramref name="assembly"/> belongs to <paramref name="info"/>: listed among the mod's declared
    /// assemblies, or loaded from under the mod's directory (covers the loader + <c>lib/&lt;ver&gt;/Content.dll</c> split
    /// used by YuWanCard/ExtractionRun-style bundles — on 0.107.1's single-<c>assembly</c> model the content assembly is
    /// never a declared mod assembly). 判断程序集是否属于该 mod：在声明的程序集列表里，或从 mod 目录下加载（覆盖
    /// YuWanCard/ExtractionRun 式「loader + lib/版本/Content.dll」拆分——0.107.1 只有单 assembly，内容程序集不在其中）。
    /// </summary>
    private static bool BelongsToMod(Assembly assembly, LoadedModInfo info)
    {
        if (info.Assemblies.Contains(assembly))
        {
            return true;
        }

        if (info.Path is string path && path.Length > 0)
        {
            try
            {
                string? location = assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    // Trailing separator so a mod dir never prefixes another dir sharing its name. 尾随分隔符，避免同名前缀目录误配。
                    string dir = path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    return location.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                // Location can throw for dynamic assemblies — treat as no match.
            }
        }

        return false;
    }

    /// <summary>Whether the mod actually loaded (state unreadable → assume yes, preserving prior behavior).
    /// 该 mod 是否真的加载成功（state 读不到时假定已加载，保持旧行为）。</summary>
    private static bool IsLoaded(Mod mod)
    {
        try
        {
            if (typeof(Mod).GetField("state", FieldFlags)?.GetValue(mod) is ModLoadState state)
            {
                return state == ModLoadState.Loaded;
            }
        }
        catch (Exception)
        {
            // Fall through to the optimistic default below.
        }

        return true;
    }

    private static string? ModPath(Mod mod)
    {
        try
        {
            return typeof(Mod).GetField("path", FieldFlags)?.GetValue(mod) is string path && path.Length > 0
                ? path
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Candidate entry prefixes for a mod: the normalized stem (<c>WHAT_IF_RELICS</c>), the compact id with separators
    /// removed (<c>WHATIFRELICS</c> — YuWanCard entries are <c>YUWANCARD-...</c>), and the raw manifest id. Case-insensitive.
    /// 该 mod 的 entry 候选前缀：规范化 stem、去分隔紧凑 id（YuWanCard 条目为 YUWANCARD-...）、原始 manifest id。大小写不敏感。
    /// </summary>
    private static HashSet<string> BuildPrefixes(string id, string stem)
    {
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (stem.Length > 0)
        {
            prefixes.Add(stem);
        }

        string compact = new string(id.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length > 0)
        {
            prefixes.Add(compact);
        }

        prefixes.Add(id);
        return prefixes;
    }

    private sealed class LoadedModInfo
    {
        public required string Stem { get; init; }

        public required string DisplayName { get; init; }

        public string? Path { get; init; }

        public IReadOnlyList<Assembly> Assemblies { get; init; } = Array.Empty<Assembly>();

        public HashSet<string> Prefixes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>The resolved content source of a warehouse item. 仓库物品的内容来源判定结果。</summary>
public enum ContentSourceKind
{
    /// <summary>Owned by a loaded mod. 属于某个已加载 mod。</summary>
    Mod,

    /// <summary>Base-game content. 原版内容。</summary>
    Base,

    /// <summary>Neither — the model can't be resolved and no mod prefix matches (e.g. an uninstalled mod's leftover).
    /// 无法判定——模型解析不到且无 mod 前缀匹配（如已卸载 mod 的残留）。</summary>
    Unknown,
}

/// <summary>
/// A warehouse item's content source, used by the hub's 内容来源 filter. <see cref="SourceKey"/> is the stable option
/// value: <see cref="BaseKey"/> / <see cref="UnknownKey"/> / the owning mod's normalized stem.
/// 仓库物品的内容来源（供「内容来源」过滤使用）：SourceKey 为稳定选项值——base / unknown / 归属 mod 的规范化 stem。
/// </summary>
public readonly record struct ContentSource(ContentSourceKind Kind, string? ModStem)
{
    public const string BaseKey = "base";
    public const string UnknownKey = "unknown";

    /// <summary>True when the item belongs to a loaded mod. 是否属于某已加载 mod。</summary>
    public bool IsMod => Kind == ContentSourceKind.Mod;

    /// <summary>Stable filter option value for this source. 该来源的稳定过滤选项值。</summary>
    public string SourceKey => Kind switch
    {
        ContentSourceKind.Mod => ModStem ?? UnknownKey,
        ContentSourceKind.Base => BaseKey,
        _ => UnknownKey,
    };

    public static ContentSource Mod(string stem) => new(ContentSourceKind.Mod, stem);

    public static ContentSource Base => new(ContentSourceKind.Base, null);

    public static ContentSource Unknown => new(ContentSourceKind.Unknown, null);
}
